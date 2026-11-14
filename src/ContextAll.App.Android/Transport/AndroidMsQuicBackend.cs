using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using ContextAll.Core;
using ContextAll.Effects;
using Microsoft.Quic;

namespace ContextAll.App.Android.Transport;

public sealed class AndroidMsQuicBackend : IPortableQuicBackend
{
    public static readonly AndroidMsQuicBackend Instance = new();

    static readonly byte[] AlpnBytes = Encoding.ASCII.GetBytes(QuicLink.Alpn);
    static readonly byte[] PfxPasswordUtf8 = Encoding.ASCII.GetBytes("contextall\0");
    static readonly byte[] SniUtf8 = Encoding.ASCII.GetBytes("contextall\0");

    readonly object gate = new();
    unsafe QUIC_API_TABLE* apiTable;
    unsafe QUIC_HANDLE* registration;
    bool initialized;
    bool supported;

    AndroidMsQuicBackend()
    {
    }

    public static void Register()
    {
        QuicLink.UseBackend(Instance);
    }

    public bool IsSupported
    {
        get
        {
            EnsureInitialized();
            return supported;
        }
    }

    public Task<IQuicListener> ListenMutualAsync(
        IPAddress address,
        X509Certificate2 certificate,
        int port = QuicLink.Port,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var credential = new PortableCredential(certificate.Export(X509ContentType.Pfx, "contextall"));
        return Task.FromResult(CreateListener(address, credential, port));
    }

    public Task<IQuicListener> ListenMutualPortableAsync(
        IPAddress address,
        PortableCredential credential,
        int port = QuicLink.Port,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateListener(address, credential, port));
    }

    public async Task<(IQuicConnection Connection, X509Certificate2 Peer)> ConnectMutualAsync(
        IPEndPoint remote,
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default)
    {
        var credential = new PortableCredential(certificate.Export(X509ContentType.Pfx, "contextall"));
        var (connection, peerDer) = await ConnectMutualCoreAsync(remote, credential, cancellationToken).ConfigureAwait(false);
        if (connection.PeerCertificate is not { } peer)
        {
            _ = peerDer;
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("peer-certificate");
        }

        return (connection, peer);
    }

    public async Task<(IQuicConnection Connection, byte[] PeerDer)> ConnectMutualPortableAsync(
        IPEndPoint remote,
        PortableCredential credential,
        CancellationToken cancellationToken = default)
    {
        var (connection, peerDer) = await ConnectMutualCoreAsync(remote, credential, cancellationToken).ConfigureAwait(false);
        return (connection, peerDer);
    }

    async Task<(MsQuicConnectionImpl Connection, byte[] PeerDer)> ConnectMutualCoreAsync(
        IPEndPoint remote,
        PortableCredential credential,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = CreateClientConnection(remote, credential);
        try
        {
            await connection.ConnectAsync(remote, cancellationToken).ConfigureAwait(false);
            if (connection.PeerCertificateDer is not { Length: > 0 } peerDer)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException("peer-certificate");
            }

            return (connection, peerDer);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    unsafe IQuicListener CreateListener(IPAddress address, PortableCredential credential, int port)
    {
        var api = GetApiOrThrow();
        var config = OpenConfiguration(api, registration, credential, isClient: false, peerBidiStreamCount: 1);
        try
        {
            return new MsQuicListenerImpl(api, registration, config, new IPEndPoint(address, port));
        }
        catch
        {
            api->ConfigurationClose(config);
            throw;
        }
    }

    unsafe MsQuicConnectionImpl CreateClientConnection(IPEndPoint remote, PortableCredential credential)
    {
        var api = GetApiOrThrow();
        var config = OpenConfiguration(api, registration, credential, isClient: true, peerBidiStreamCount: 0);
        try
        {
            return new MsQuicConnectionImpl(api, registration, config, ownsConfig: true, remote);
        }
        catch
        {
            api->ConfigurationClose(config);
            throw;
        }
    }

    unsafe QUIC_API_TABLE* GetApiOrThrow()
    {
        EnsureInitialized();
        if (!supported || apiTable == null || registration == null)
        {
            throw new InvalidOperationException("quic-unsupported");
        }

        return apiTable;
    }

    unsafe void EnsureInitialized()
    {
        lock (gate)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            try
            {
                var opened = MsQuic.Open();
                if (opened == null)
                {
                    PlatformCrypto.LogDiagnostic("MsQuic.Open() returned null");
                    supported = false;
                    return;
                }

                var appNameBytes = Encoding.ASCII.GetBytes("contextall\0");
                QUIC_HANDLE* reg = null;
                fixed (byte* appNamePtr = appNameBytes)
                {
                    var regConfig = new QUIC_REGISTRATION_CONFIG
                    {
                        AppName = (sbyte*)appNamePtr,
                        ExecutionProfile = QUIC_EXECUTION_PROFILE.LOW_LATENCY,
                    };
                    var status = opened->RegistrationOpen(&regConfig, &reg);
                    if (MsQuic.StatusFailed(status) || reg == null)
                    {
                        PlatformCrypto.LogDiagnostic($"MsQuic RegistrationOpen failed: 0x{status:X8}");
                        MsQuic.Close(opened);
                        supported = false;
                        return;
                    }
                }

                apiTable = opened;
                registration = reg;
                supported = true;
                PlatformCrypto.LogDiagnostic("MsQuic initialized OK");
            }
            catch (Exception ex)
            {
                PlatformCrypto.LogDiagnostic($"MsQuic init exception: {ex.GetType().Name} {ex.Message}");
                supported = false;
            }
        }
    }

    static unsafe QUIC_HANDLE* OpenConfiguration(
        QUIC_API_TABLE* api,
        QUIC_HANDLE* reg,
        PortableCredential credential,
        bool isClient,
        ushort peerBidiStreamCount)
    {
        var settings = default(QUIC_SETTINGS);
        settings.IdleTimeoutMs = 60_000;
        settings.IsSet.IdleTimeoutMs = 1;
        settings.KeepAliveIntervalMs = 15_000;
        settings.IsSet.KeepAliveIntervalMs = 1;
        settings.PeerBidiStreamCount = peerBidiStreamCount;
        settings.IsSet.PeerBidiStreamCount = 1;

        QUIC_HANDLE* config = null;
        fixed (byte* alpnPtr = AlpnBytes)
        {
            var alpnBuffer = new QUIC_BUFFER
            {
                Length = (uint)AlpnBytes.Length,
                Buffer = alpnPtr,
            };
            var status = api->ConfigurationOpen(
                reg,
                &alpnBuffer,
                1,
                &settings,
                (uint)sizeof(QUIC_SETTINGS),
                null,
                &config);
            if (MsQuic.StatusFailed(status) || config == null)
            {
                throw new IOException($"MsQuic ConfigurationOpen failed: 0x{status:X8}");
            }
        }

        try
        {
            var flags = QUIC_CREDENTIAL_FLAGS.NO_CERTIFICATE_VALIDATION
                | QUIC_CREDENTIAL_FLAGS.INDICATE_CERTIFICATE_RECEIVED
                | QUIC_CREDENTIAL_FLAGS.USE_PORTABLE_CERTIFICATES;
            flags |= isClient
                ? QUIC_CREDENTIAL_FLAGS.CLIENT
                : QUIC_CREDENTIAL_FLAGS.REQUIRE_CLIENT_AUTHENTICATION;

            int credStatus;
            if (!string.IsNullOrWhiteSpace(credential.CertificatePemPath)
                && !string.IsNullOrWhiteSpace(credential.PrivateKeyPemPath))
            {
                var certBytes = Encoding.UTF8.GetBytes(credential.CertificatePemPath + "\0");
                var keyBytes = Encoding.UTF8.GetBytes(credential.PrivateKeyPemPath + "\0");
                fixed (byte* certPtr = certBytes)
                fixed (byte* keyPtr = keyBytes)
                {
                    var certFile = new QUIC_CERTIFICATE_FILE
                    {
                        PrivateKeyFile = (sbyte*)keyPtr,
                        CertificateFile = (sbyte*)certPtr,
                    };
                    var cred = new QUIC_CREDENTIAL_CONFIG
                    {
                        Type = QUIC_CREDENTIAL_TYPE.CERTIFICATE_FILE,
                        Flags = flags,
                    };
                    cred.CertificateFile = &certFile;
                    credStatus = api->ConfigurationLoadCredential(config, &cred);
                }
            }
            else if (credential.PfxBytes is { Length: > 0 } pfx)
            {
                fixed (byte* pfxPtr = pfx)
                fixed (byte* pwdPtr = PfxPasswordUtf8)
                {
                    var pkcs12 = new QUIC_CERTIFICATE_PKCS12
                    {
                        Asn1Blob = pfxPtr,
                        Asn1BlobLength = (uint)pfx.Length,
                        PrivateKeyPassword = (sbyte*)pwdPtr,
                    };
                    var cred = new QUIC_CREDENTIAL_CONFIG
                    {
                        Type = QUIC_CREDENTIAL_TYPE.CERTIFICATE_PKCS12,
                        Flags = flags,
                    };
                    cred.CertificatePkcs12 = &pkcs12;
                    credStatus = api->ConfigurationLoadCredential(config, &cred);
                }
            }
            else
            {
                throw new InvalidOperationException("missing-credential");
            }

            if (MsQuic.StatusFailed(credStatus))
            {
                throw new IOException($"MsQuic ConfigurationLoadCredential failed: 0x{credStatus:X8}");
            }

            return config;
        }
        catch
        {
            api->ConfigurationClose(config);
            throw;
        }
    }

    internal static unsafe QuicAddr ToQuicAddr(IPEndPoint endpoint)
    {
        var addr = default(QuicAddr);
        var ip = endpoint.Address;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            addr.Family = MsQuic.QUIC_ADDRESS_FAMILY_INET;
            addr.Ipv4.sin_port = BinaryPrimitives.ReverseEndianness((ushort)endpoint.Port);
            var dest = new Span<byte>(addr.Ipv4.sin_addr, 4);
            ip.TryWriteBytes(dest, out _);
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            addr.Family = MsQuic.QUIC_ADDRESS_FAMILY_INET6;
            addr.Ipv6.sin6_port = BinaryPrimitives.ReverseEndianness((ushort)endpoint.Port);
            addr.Ipv6.sin6_scope_id = (uint)ip.ScopeId;
            var dest = new Span<byte>(addr.Ipv6.sin6_addr, 16);
            ip.TryWriteBytes(dest, out _);
        }
        else
        {
            throw new NotSupportedException($"Unsupported address family: {ip.AddressFamily}");
        }

        return addr;
    }

    internal static unsafe IPEndPoint? FromQuicAddr(QuicAddr* addr)
    {
        if (addr == null)
        {
            return null;
        }

        var family = addr->Family;
        if (family == MsQuic.QUIC_ADDRESS_FAMILY_INET)
        {
            var bytes = new ReadOnlySpan<byte>(addr->Ipv4.sin_addr, 4);
            var port = BinaryPrimitives.ReverseEndianness(addr->Ipv4.sin_port);
            return new IPEndPoint(new IPAddress(bytes), port);
        }

        if (family == MsQuic.QUIC_ADDRESS_FAMILY_INET6)
        {
            var bytes = new ReadOnlySpan<byte>(addr->Ipv6.sin6_addr, 16);
            var port = BinaryPrimitives.ReverseEndianness(addr->Ipv6.sin6_port);
            return new IPEndPoint(new IPAddress(bytes, addr->Ipv6.sin6_scope_id), port);
        }

        return null;
    }

    sealed class MsQuicListenerImpl : IQuicListener
    {
        readonly unsafe QUIC_API_TABLE* api;
        readonly unsafe QUIC_HANDLE* config;
        readonly Channel<IQuicConnection> accepted = Channel.CreateUnbounded<IQuicConnection>();
        readonly TaskCompletionSource stopCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly object stateGate = new();
        unsafe QUIC_HANDLE* handle;
        GCHandle selfHandle;
        bool stopping;
        bool closed;

        public unsafe MsQuicListenerImpl(
            QUIC_API_TABLE* api,
            QUIC_HANDLE* registration,
            QUIC_HANDLE* config,
            IPEndPoint listenEndPoint)
        {
            this.api = api;
            this.config = config;
            selfHandle = GCHandle.Alloc(this);

            QUIC_HANDLE* opened = null;
            var status = api->ListenerOpen(
                registration,
                &ListenerCallback,
                (void*)GCHandle.ToIntPtr(selfHandle),
                &opened);
            if (MsQuic.StatusFailed(status) || opened == null)
            {
                selfHandle.Free();
                throw new IOException($"MsQuic ListenerOpen failed: 0x{status:X8}");
            }

            handle = opened;
            try
            {
                var localAddr = ToQuicAddr(listenEndPoint);
                fixed (byte* alpnPtr = AlpnBytes)
                {
                    var alpnBuf = new QUIC_BUFFER
                    {
                        Length = (uint)AlpnBytes.Length,
                        Buffer = alpnPtr,
                    };
                    var startStatus = api->ListenerStart(handle, &alpnBuf, 1, &localAddr);
                    if (MsQuic.StatusFailed(startStatus))
                    {
                        throw new IOException($"MsQuic ListenerStart {listenEndPoint} failed: 0x{startStatus:X8}");
                    }
                }

                QuicAddr boundAddr = default;
                var boundSize = (uint)sizeof(QuicAddr);
                var getStatus = api->GetParam(handle, MsQuic.QUIC_PARAM_LISTENER_LOCAL_ADDRESS, &boundSize, &boundAddr);
                LocalEndPoint = MsQuic.StatusSucceeded(getStatus) && FromQuicAddr(&boundAddr) is { } actual
                    ? actual
                    : listenEndPoint;
            }
            catch
            {
                api->ListenerClose(handle);
                handle = null;
                closed = true;
                if (selfHandle.IsAllocated)
                {
                    selfHandle.Free();
                }

                throw;
            }
        }

        public IPEndPoint LocalEndPoint { get; }

        public async ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default)
        {
            while (await accepted.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (accepted.Reader.TryRead(out var connection))
                {
                    return connection;
                }
            }

            throw new ObjectDisposedException(nameof(MsQuicListenerImpl));
        }

        public async ValueTask DisposeAsync()
        {
            BeginStopNative();
            if (!IsClosedSnapshot())
            {
                await stopCompleted.Task.ConfigureAwait(false);
            }
        }

        bool IsClosedSnapshot()
        {
            lock (stateGate)
            {
                return closed;
            }
        }

        unsafe void BeginStopNative()
        {
            QUIC_HANDLE* current = null;
            lock (stateGate)
            {
                if (!stopping && !closed)
                {
                    stopping = true;
                    current = handle;
                }
            }

            accepted.Writer.TryComplete();
            if (current != null)
            {
                api->ListenerStop(current);
            }
        }

        internal void PublishAccepted(MsQuicConnectionImpl connection)
        {
            if (!accepted.Writer.TryWrite(connection))
            {
                _ = connection.DisposeAsync().AsTask();
            }
        }

        unsafe int OnEvent(QUIC_LISTENER_EVENT* ev)
        {
            switch (ev->Type)
            {
                case QUIC_LISTENER_EVENT_TYPE.NEW_CONNECTION:
                    lock (stateGate)
                    {
                        if (stopping || closed)
                        {
                            return MsQuic.QUIC_STATUS_CONNECTION_REFUSED;
                        }
                    }

                    var connHandle = ev->NEW_CONNECTION.Connection;
                    var remote = ev->NEW_CONNECTION.Info != null
                        ? FromQuicAddr(ev->NEW_CONNECTION.Info->RemoteAddress)
                        : null;
                    var serverConn = new MsQuicConnectionImpl(api, connHandle, remote, this);
                    var cfgStatus = api->ConnectionSetConfiguration(connHandle, config);
                    if (MsQuic.StatusFailed(cfgStatus))
                    {
                        serverConn.ReleaseAbortedBeforeStart();
                        return cfgStatus;
                    }

                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_LISTENER_EVENT_TYPE.STOP_COMPLETE:
                    lock (stateGate)
                    {
                        if (!closed)
                        {
                            closed = true;
                            if (handle != null)
                            {
                                api->ListenerClose(handle);
                                handle = null;
                            }

                            api->ConfigurationClose(config);
                            if (selfHandle.IsAllocated)
                            {
                                selfHandle.Free();
                            }
                        }
                    }

                    stopCompleted.TrySetResult();
                    return MsQuic.QUIC_STATUS_SUCCESS;

                default:
                    return MsQuic.QUIC_STATUS_SUCCESS;
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        static unsafe int ListenerCallback(QUIC_HANDLE* listener, void* context, QUIC_LISTENER_EVENT* ev)
        {
            if (context == null)
            {
                return MsQuic.QUIC_STATUS_INTERNAL_ERROR;
            }

            var target = GCHandle.FromIntPtr((IntPtr)context).Target as MsQuicListenerImpl;
            return target is null ? MsQuic.QUIC_STATUS_INTERNAL_ERROR : target.OnEvent(ev);
        }
    }

    sealed class MsQuicConnectionImpl : IPortableQuicConnection
    {
        readonly unsafe QUIC_API_TABLE* api;
        readonly unsafe QUIC_HANDLE* config;
        readonly bool ownsConfig;
        readonly MsQuicListenerImpl? parentListener;
        readonly Channel<Stream> inboundStreams = Channel.CreateUnbounded<Stream>();
        readonly TaskCompletionSource connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource shutdownCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly object stateGate = new();
        unsafe QUIC_HANDLE* handle;
        GCHandle selfHandle;
        IPEndPoint? remoteEndPoint;
        byte[]? peerCertificateDer;
        X509Certificate2? peerCertificate;
        bool shutdownStarted;
        bool closed;

        public unsafe MsQuicConnectionImpl(
            QUIC_API_TABLE* api,
            QUIC_HANDLE* registration,
            QUIC_HANDLE* config,
            bool ownsConfig,
            IPEndPoint remote)
        {
            this.api = api;
            this.config = config;
            this.ownsConfig = ownsConfig;
            remoteEndPoint = remote;
            selfHandle = GCHandle.Alloc(this);

            QUIC_HANDLE* opened = null;
            var status = api->ConnectionOpen(
                registration,
                &ConnectionCallback,
                (void*)GCHandle.ToIntPtr(selfHandle),
                &opened);
            if (MsQuic.StatusFailed(status) || opened == null)
            {
                selfHandle.Free();
                throw new IOException($"MsQuic ConnectionOpen failed: 0x{status:X8}");
            }

            handle = opened;
        }

        public unsafe MsQuicConnectionImpl(
            QUIC_API_TABLE* api,
            QUIC_HANDLE* acceptedHandle,
            IPEndPoint? remote,
            MsQuicListenerImpl listener)
        {
            this.api = api;
            config = null;
            ownsConfig = false;
            parentListener = listener;
            remoteEndPoint = remote;
            handle = acceptedHandle;
            selfHandle = GCHandle.Alloc(this);
            MsQuicExtensions.SetConnectionCallback(
                ref *api,
                acceptedHandle,
                &ConnectionCallback,
                (void*)GCHandle.ToIntPtr(selfHandle));
        }

        public EndPoint? RemoteEndPoint => remoteEndPoint;

        public byte[]? PeerCertificateDer => peerCertificateDer;

        public X509Certificate2? PeerCertificate =>
            peerCertificate ??= peerCertificateDer is { Length: > 0 } der
                ? X509CertificateLoader.LoadCertificate(der)
                : null;

        internal unsafe void ReleaseAbortedBeforeStart()
        {
            lock (stateGate)
            {
                closed = true;
                handle = null;
                if (selfHandle.IsAllocated)
                {
                    selfHandle.Free();
                }
            }

            shutdownCompleted.TrySetResult();
        }

        public async Task ConnectAsync(IPEndPoint remote, CancellationToken cancellationToken)
        {
            StartConnectNative(remote);
            await using var reg = cancellationToken.Register(() => InitiateShutdown(1));
            await connectedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        unsafe void StartConnectNative(IPEndPoint remote)
        {
            var remoteAddr = ToQuicAddr(remote);
            var setStatus = api->SetParam(
                handle,
                MsQuic.QUIC_PARAM_CONN_REMOTE_ADDRESS,
                (uint)sizeof(QuicAddr),
                &remoteAddr);
            if (MsQuic.StatusFailed(setStatus))
            {
                throw new IOException($"MsQuic SetParam(CONN_REMOTE_ADDRESS) failed: 0x{setStatus:X8}");
            }

            fixed (byte* sniPtr = SniUtf8)
            {
                var startStatus = api->ConnectionStart(
                    handle,
                    config,
                    (ushort)remoteAddr.Family,
                    (sbyte*)sniPtr,
                    (ushort)remote.Port);
                if (MsQuic.StatusFailed(startStatus))
                {
                    throw new IOException($"MsQuic ConnectionStart failed: 0x{startStatus:X8}");
                }
            }
        }

        public async ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = CreateOutboundStreamNative();
            try
            {
                await stream.StartOutboundAsync(cancellationToken).ConfigureAwait(false);
                return stream;
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        unsafe MsQuicStreamImpl CreateOutboundStreamNative()
        {
            lock (stateGate)
            {
                if (closed || shutdownStarted || handle == null)
                {
                    throw new ObjectDisposedException(nameof(MsQuicConnectionImpl));
                }

                return new MsQuicStreamImpl(api, handle);
            }
        }

        public async ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken = default)
        {
            while (await inboundStreams.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (inboundStreams.Reader.TryRead(out var stream))
                {
                    return stream;
                }
            }

            throw new IOException("QUIC connection closed before inbound stream arrived.");
        }

        public async ValueTask DisposeAsync()
        {
            InitiateShutdown(0);
            if (!IsClosedSnapshot())
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await shutdownCompleted.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        bool IsClosedSnapshot()
        {
            lock (stateGate)
            {
                return closed;
            }
        }

        unsafe void InitiateShutdown(ulong errorCode)
        {
            QUIC_HANDLE* current;
            lock (stateGate)
            {
                if (closed || shutdownStarted || handle == null)
                {
                    return;
                }

                shutdownStarted = true;
                current = handle;
            }

            inboundStreams.Writer.TryComplete();
            api->ConnectionShutdown(current, QUIC_CONNECTION_SHUTDOWN_FLAGS.NONE, errorCode);
        }

        unsafe int OnEvent(QUIC_CONNECTION_EVENT* ev)
        {
            switch (ev->Type)
            {
                case QUIC_CONNECTION_EVENT_TYPE.PEER_CERTIFICATE_RECEIVED:
                    try
                    {
                        var certBuf = (QUIC_BUFFER*)ev->PEER_CERTIFICATE_RECEIVED.Certificate;
                        if (certBuf != null && certBuf->Buffer != null && certBuf->Length > 0)
                        {
                            var der = new ReadOnlySpan<byte>(certBuf->Buffer, checked((int)certBuf->Length));
                            peerCertificateDer = der.ToArray();
                        }
                    }
                    catch
                    {
                        return MsQuic.QUIC_STATUS_BAD_CERTIFICATE;
                    }

                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_CONNECTION_EVENT_TYPE.PEER_ADDRESS_CHANGED:
                    if (FromQuicAddr(ev->PEER_ADDRESS_CHANGED.Address) is { } updatedRemote)
                    {
                        remoteEndPoint = updatedRemote;
                    }

                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_CONNECTION_EVENT_TYPE.CONNECTED:
                    if (remoteEndPoint is null && handle != null)
                    {
                        QuicAddr addr = default;
                        var len = (uint)sizeof(QuicAddr);
                        if (MsQuic.StatusSucceeded(api->GetParam(handle, MsQuic.QUIC_PARAM_CONN_REMOTE_ADDRESS, &len, &addr)))
                        {
                            remoteEndPoint = FromQuicAddr(&addr);
                        }
                    }

                    connectedTcs.TrySetResult();
                    parentListener?.PublishAccepted(this);
                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_CONNECTION_EVENT_TYPE.PEER_STREAM_STARTED:
                    if ((ev->PEER_STREAM_STARTED.Flags & QUIC_STREAM_OPEN_FLAGS.UNIDIRECTIONAL) != 0)
                    {
                        return MsQuic.QUIC_STATUS_NOT_SUPPORTED;
                    }

                    var inbound = new MsQuicStreamImpl(api, ev->PEER_STREAM_STARTED.Stream, isInbound: true);
                    if (!inboundStreams.Writer.TryWrite(inbound))
                    {
                        _ = inbound.DisposeAsync().AsTask();
                    }

                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_CONNECTION_EVENT_TYPE.SHUTDOWN_INITIATED_BY_TRANSPORT:
                    connectedTcs.TrySetException(new IOException($"QUIC transport shutdown: 0x{ev->SHUTDOWN_INITIATED_BY_TRANSPORT.Status:X8}"));
                    inboundStreams.Writer.TryComplete();
                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_CONNECTION_EVENT_TYPE.SHUTDOWN_INITIATED_BY_PEER:
                    connectedTcs.TrySetException(new IOException($"QUIC peer shutdown: {ev->SHUTDOWN_INITIATED_BY_PEER.ErrorCode}"));
                    inboundStreams.Writer.TryComplete();
                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_CONNECTION_EVENT_TYPE.SHUTDOWN_COMPLETE:
                    connectedTcs.TrySetException(new IOException("QUIC connection closed."));
                    inboundStreams.Writer.TryComplete();
                    lock (stateGate)
                    {
                        if (!closed)
                        {
                            closed = true;
                            if (handle != null)
                            {
                                api->ConnectionClose(handle);
                                handle = null;
                            }

                            if (ownsConfig && config != null)
                            {
                                api->ConfigurationClose(config);
                            }

                            if (selfHandle.IsAllocated)
                            {
                                selfHandle.Free();
                            }
                        }
                    }

                    shutdownCompleted.TrySetResult();
                    return MsQuic.QUIC_STATUS_SUCCESS;

                default:
                    return MsQuic.QUIC_STATUS_SUCCESS;
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        static unsafe int ConnectionCallback(QUIC_HANDLE* connection, void* context, QUIC_CONNECTION_EVENT* ev)
        {
            if (context == null)
            {
                return MsQuic.QUIC_STATUS_INTERNAL_ERROR;
            }

            var target = GCHandle.FromIntPtr((IntPtr)context).Target as MsQuicConnectionImpl;
            return target is null ? MsQuic.QUIC_STATUS_INTERNAL_ERROR : target.OnEvent(ev);
        }
    }

    sealed class MsQuicStreamImpl : Stream
    {
        const int PauseReceiveThreshold = 1024 * 1024;
        const int ResumeReceiveThreshold = 256 * 1024;

        readonly unsafe QUIC_API_TABLE* api;
        readonly object recvGate = new();
        readonly Queue<byte[]> recvChunks = new();
        readonly SemaphoreSlim recvSignal = new(0);
        readonly TaskCompletionSource startTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource shutdownTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        unsafe QUIC_HANDLE* handle;
        GCHandle selfHandle;
        byte[]? currentChunk;
        int currentOffset;
        int bufferedBytes;
        bool recvPaused;
        bool eof;
        bool aborted;
        bool shutdownStarted;
        bool closed;

        public unsafe MsQuicStreamImpl(QUIC_API_TABLE* api, QUIC_HANDLE* connectionHandle)
        {
            this.api = api;
            selfHandle = GCHandle.Alloc(this);
            QUIC_HANDLE* opened = null;
            var status = api->StreamOpen(
                connectionHandle,
                QUIC_STREAM_OPEN_FLAGS.NONE,
                &StreamCallback,
                (void*)GCHandle.ToIntPtr(selfHandle),
                &opened);
            if (MsQuic.StatusFailed(status) || opened == null)
            {
                selfHandle.Free();
                throw new IOException($"MsQuic StreamOpen failed: 0x{status:X8}");
            }

            handle = opened;
        }

        public unsafe MsQuicStreamImpl(QUIC_API_TABLE* api, QUIC_HANDLE* inboundHandle, bool isInbound)
        {
            _ = isInbound;
            this.api = api;
            handle = inboundHandle;
            selfHandle = GCHandle.Alloc(this);
            MsQuicExtensions.SetStreamCallback(
                ref *api,
                inboundHandle,
                &StreamCallback,
                (void*)GCHandle.ToIntPtr(selfHandle));
            startTcs.TrySetResult();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public async Task StartOutboundAsync(CancellationToken cancellationToken)
        {
            StartStreamNative();
            await startTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        unsafe void StartStreamNative()
        {
            var status = api->StreamStart(handle, QUIC_STREAM_START_FLAGS.IMMEDIATE | QUIC_STREAM_START_FLAGS.SHUTDOWN_ON_FAIL);
            if (MsQuic.StatusFailed(status))
            {
                throw new IOException($"MsQuic StreamStart failed: 0x{status:X8}");
            }
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0)
            {
                return 0;
            }

            while (true)
            {
                if (TryReadBuffered(buffer.Span, out var copied, out var reachedEof))
                {
                    return copied;
                }

                if (reachedEof)
                {
                    return 0;
                }

                await recvSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        unsafe bool TryReadBuffered(Span<byte> destination, out int copied, out bool reachedEof)
        {
            copied = 0;
            reachedEof = false;
            var shouldResume = false;
            QUIC_HANDLE* streamPtr = null;

            lock (recvGate)
            {
                if (currentChunk is null && recvChunks.Count > 0)
                {
                    currentChunk = recvChunks.Dequeue();
                    currentOffset = 0;
                }

                if (currentChunk is not null)
                {
                    var available = currentChunk.Length - currentOffset;
                    var toCopy = Math.Min(available, destination.Length);
                    currentChunk.AsSpan(currentOffset, toCopy).CopyTo(destination);
                    currentOffset += toCopy;
                    bufferedBytes -= toCopy;
                    if (currentOffset >= currentChunk.Length)
                    {
                        currentChunk = null;
                        currentOffset = 0;
                    }

                    if (recvPaused && bufferedBytes <= ResumeReceiveThreshold && !closed && handle != null)
                    {
                        recvPaused = false;
                        shouldResume = true;
                        streamPtr = handle;
                    }

                    if (shouldResume && streamPtr != null)
                    {
                        api->StreamReceiveSetEnabled(streamPtr, 1);
                    }

                    copied = toCopy;
                    return true;
                }

                if (aborted)
                {
                    throw new IOException("QUIC stream aborted.");
                }

                if (eof || closed)
                {
                    reachedEof = true;
                    return false;
                }
            }

            return false;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Use ReadAsync on QUIC streams.");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Use WriteAsync on QUIC streams.");

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length == 0)
            {
                return;
            }

            var completion = BeginSendNative(buffer.ToArray());
            await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        unsafe Task BeginSendNative(byte[] copy)
        {
            QUIC_HANDLE* streamPtr;
            lock (recvGate)
            {
                if (closed || shutdownStarted || handle == null)
                {
                    throw new IOException("QUIC stream is closed.");
                }

                streamPtr = handle;
            }

            var sendState = new SendOperation(copy);
            var sendHandle = GCHandle.Alloc(sendState);
            var status = api->StreamSend(
                streamPtr,
                sendState.NativeBuffer,
                1,
                QUIC_SEND_FLAGS.NONE,
                (void*)GCHandle.ToIntPtr(sendHandle));
            if (MsQuic.StatusFailed(status))
            {
                sendHandle.Free();
                sendState.Dispose();
                throw new IOException($"MsQuic StreamSend failed: 0x{status:X8}");
            }

            return sendState.Completion.Task;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                BeginShutdown();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            BeginShutdown();
            if (!IsClosedSnapshot())
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await shutdownTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }

        bool IsClosedSnapshot()
        {
            lock (recvGate)
            {
                return closed;
            }
        }

        unsafe void BeginShutdown()
        {
            QUIC_HANDLE* streamPtr;
            lock (recvGate)
            {
                if (closed || shutdownStarted || handle == null)
                {
                    return;
                }

                shutdownStarted = true;
                eof = true;
                streamPtr = handle;
            }

            SignalReader();
            api->StreamShutdown(streamPtr, QUIC_STREAM_SHUTDOWN_FLAGS.GRACEFUL, 0);
            api->StreamShutdown(streamPtr, QUIC_STREAM_SHUTDOWN_FLAGS.ABORT_RECEIVE | QUIC_STREAM_SHUTDOWN_FLAGS.IMMEDIATE, 0);
        }

        void SignalReader()
        {
            try
            {
                recvSignal.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        unsafe int OnEvent(QUIC_STREAM_EVENT* ev)
        {
            switch (ev->Type)
            {
                case QUIC_STREAM_EVENT_TYPE.START_COMPLETE:
                    if (MsQuic.StatusFailed(ev->START_COMPLETE.Status))
                    {
                        startTcs.TrySetException(new IOException($"MsQuic stream start failed: 0x{ev->START_COMPLETE.Status:X8}"));
                    }
                    else
                    {
                        startTcs.TrySetResult();
                    }

                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_STREAM_EVENT_TYPE.RECEIVE:
                    var count = ev->RECEIVE.BufferCount;
                    var total = checked((int)ev->RECEIVE.TotalBufferLength);
                    byte[]? chunk = null;
                    if (total > 0 && count > 0 && ev->RECEIVE.Buffers != null)
                    {
                        chunk = new byte[total];
                        var offset = 0;
                        for (uint i = 0; i < count; i++)
                        {
                            var buf = ev->RECEIVE.Buffers[i];
                            if (buf.Length > 0 && buf.Buffer != null)
                            {
                                var len = checked((int)buf.Length);
                                new ReadOnlySpan<byte>(buf.Buffer, len).CopyTo(chunk.AsSpan(offset, len));
                                offset += len;
                            }
                        }
                    }

                    var fin = (ev->RECEIVE.Flags & QUIC_RECEIVE_FLAGS.FIN) != 0;
                    var shouldPause = false;
                    QUIC_HANDLE* streamPtr = null;
                    lock (recvGate)
                    {
                        if (chunk is { Length: > 0 })
                        {
                            recvChunks.Enqueue(chunk);
                            bufferedBytes += chunk.Length;
                            if (!recvPaused && bufferedBytes >= PauseReceiveThreshold && !closed && handle != null)
                            {
                                recvPaused = true;
                                shouldPause = true;
                                streamPtr = handle;
                            }
                        }

                        if (fin)
                        {
                            eof = true;
                        }
                    }

                    if (shouldPause && streamPtr != null)
                    {
                        api->StreamReceiveSetEnabled(streamPtr, 0);
                    }

                    SignalReader();
                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_STREAM_EVENT_TYPE.SEND_COMPLETE:
                    var sendCtx = ev->SEND_COMPLETE.ClientContext;
                    if (sendCtx != null)
                    {
                        var sendHandle = GCHandle.FromIntPtr((IntPtr)sendCtx);
                        if (sendHandle.Target is SendOperation op)
                        {
                            var canceled = ev->SEND_COMPLETE.Canceled != 0;
                            op.Dispose();
                            if (canceled)
                            {
                                op.Completion.TrySetException(new IOException("MsQuic send canceled."));
                            }
                            else
                            {
                                op.Completion.TrySetResult();
                            }
                        }

                        sendHandle.Free();
                    }

                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_STREAM_EVENT_TYPE.PEER_SEND_SHUTDOWN:
                    lock (recvGate)
                    {
                        eof = true;
                    }

                    SignalReader();
                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_STREAM_EVENT_TYPE.PEER_SEND_ABORTED:
                case QUIC_STREAM_EVENT_TYPE.PEER_RECEIVE_ABORTED:
                    lock (recvGate)
                    {
                        aborted = true;
                        eof = true;
                    }

                    SignalReader();
                    return MsQuic.QUIC_STATUS_SUCCESS;

                case QUIC_STREAM_EVENT_TYPE.SHUTDOWN_COMPLETE:
                    startTcs.TrySetException(new IOException("QUIC stream closed before start completed."));
                    lock (recvGate)
                    {
                        eof = true;
                        if (!closed)
                        {
                            closed = true;
                            if (handle != null)
                            {
                                api->StreamClose(handle);
                                handle = null;
                            }

                            if (selfHandle.IsAllocated)
                            {
                                selfHandle.Free();
                            }
                        }
                    }

                    SignalReader();
                    shutdownTcs.TrySetResult();
                    return MsQuic.QUIC_STATUS_SUCCESS;

                default:
                    return MsQuic.QUIC_STATUS_SUCCESS;
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        static unsafe int StreamCallback(QUIC_HANDLE* stream, void* context, QUIC_STREAM_EVENT* ev)
        {
            if (context == null)
            {
                return MsQuic.QUIC_STATUS_INTERNAL_ERROR;
            }

            var target = GCHandle.FromIntPtr((IntPtr)context).Target as MsQuicStreamImpl;
            return target is null ? MsQuic.QUIC_STATUS_INTERNAL_ERROR : target.OnEvent(ev);
        }

        sealed unsafe class SendOperation : IDisposable
        {
            GCHandle pinnedData;
            public QUIC_BUFFER* NativeBuffer { get; private set; }
            public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public SendOperation(byte[] data)
            {
                pinnedData = GCHandle.Alloc(data, GCHandleType.Pinned);
                NativeBuffer = (QUIC_BUFFER*)NativeMemory.Alloc((nuint)sizeof(QUIC_BUFFER));
                NativeBuffer->Length = (uint)data.Length;
                NativeBuffer->Buffer = (byte*)pinnedData.AddrOfPinnedObject();
            }

            public void Dispose()
            {
                if (NativeBuffer != null)
                {
                    NativeMemory.Free(NativeBuffer);
                    NativeBuffer = null;
                }

                if (pinnedData.IsAllocated)
                {
                    pinnedData.Free();
                }
            }
        }
    }
}
