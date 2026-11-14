using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable CS0649
#pragma warning disable CS8981

namespace Microsoft.Quic;

internal static unsafe class MsQuicExtensions
{
    public static void SetConnectionCallback(
        ref QUIC_API_TABLE api,
        QUIC_HANDLE* connection,
        delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, QUIC_CONNECTION_EVENT*, int> handler,
        void* context) =>
        api.SetCallbackHandler(connection, handler, context);

    public static void SetStreamCallback(
        ref QUIC_API_TABLE api,
        QUIC_HANDLE* stream,
        delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, QUIC_STREAM_EVENT*, int> handler,
        void* context) =>
        api.SetCallbackHandler(stream, handler, context);
}

internal static unsafe class MsQuic
{
    public const uint QUIC_API_VERSION_2 = 2;
    public const uint QUIC_PARAM_LISTENER_LOCAL_ADDRESS = 67108864;
    public const uint QUIC_PARAM_CONN_REMOTE_ADDRESS = 83886082;

    public const int QUIC_STATUS_SUCCESS = 0;
    public const int QUIC_STATUS_PENDING = -2;
    public const int QUIC_STATUS_CONTINUE = -1;
    public const int QUIC_STATUS_INTERNAL_ERROR = 5;
    public const int QUIC_STATUS_NOT_SUPPORTED = 95;
    public const int QUIC_STATUS_CONNECTION_REFUSED = 111;
    public const int QUIC_STATUS_BAD_CERTIFICATE = 200000298;

    public const ushort QUIC_ADDRESS_FAMILY_UNSPEC = 0;
    public const ushort QUIC_ADDRESS_FAMILY_INET = 2;
    public const ushort QUIC_ADDRESS_FAMILY_INET6 = 10;

    static int resolverRegistered;

    static void EnsureResolver()
    {
        if (Interlocked.Exchange(ref resolverRegistered, 1) != 0)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(MsQuic).Assembly, ResolveMsQuic);
    }

    static nint ResolveMsQuic(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "msquic", StringComparison.Ordinal))
        {
            return 0;
        }

        string[] candidates = ["libmsquic.so", "msquic", "libmsquic.so.2", "libmsquic"];
        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
            {
                return handle;
            }
        }

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool StatusSucceeded(int status) => status <= 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool StatusFailed(int status) => status > 0;

    public static QUIC_API_TABLE* Open()
    {
        EnsureResolver();
        void* table = null;
        var status = MsQuicOpenVersion(QUIC_API_VERSION_2, &table);
        if (StatusFailed(status) || table == null)
        {
            return null;
        }

        return (QUIC_API_TABLE*)table;
    }

    public static void Close(QUIC_API_TABLE* apiTable)
    {
        if (apiTable != null)
        {
            MsQuicClose(apiTable);
        }
    }

    [DllImport("msquic", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    static extern int MsQuicOpenVersion(uint version, void** quicApi);

    [DllImport("msquic", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    static extern void MsQuicClose(void* quicApi);
}

[StructLayout(LayoutKind.Sequential, Size = 1)]
internal unsafe struct QUIC_HANDLE
{
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_BUFFER
{
    public uint Length;
    public byte* Buffer;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_REGISTRATION_CONFIG
{
    public sbyte* AppName;
    public QUIC_EXECUTION_PROFILE ExecutionProfile;
}

internal enum QUIC_EXECUTION_PROFILE : int
{
    LOW_LATENCY = 0,
    MAX_THROUGHPUT = 1,
    SCAVENGER = 2,
    REAL_TIME = 3,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_CERTIFICATE_FILE
{
    public sbyte* PrivateKeyFile;
    public sbyte* CertificateFile;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_CERTIFICATE_PKCS12
{
    public byte* Asn1Blob;
    public uint Asn1BlobLength;
    public sbyte* PrivateKeyPassword;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_CREDENTIAL_CONFIG
{
    public QUIC_CREDENTIAL_TYPE Type;
    public QUIC_CREDENTIAL_FLAGS Flags;
    public _Anonymous_e__Union Anonymous;
    public sbyte* Principal;
    public void* Reserved;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, int, void> AsyncHandler;
    public QUIC_ALLOWED_CIPHER_SUITE_FLAGS AllowedCipherSuites;
    public sbyte* CaCertificateFile;

    public QUIC_CERTIFICATE_FILE* CertificateFile
    {
        readonly get => Anonymous.CertificateFile;
        set => Anonymous.CertificateFile = value;
    }

    public QUIC_CERTIFICATE_PKCS12* CertificatePkcs12
    {
        readonly get => Anonymous.CertificatePkcs12;
        set => Anonymous.CertificatePkcs12 = value;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct _Anonymous_e__Union
    {
        [FieldOffset(0)]
        public void* CertificatePtr;
        [FieldOffset(0)]
        public QUIC_CERTIFICATE_FILE* CertificateFile;
        [FieldOffset(0)]
        public QUIC_CERTIFICATE_PKCS12* CertificatePkcs12;
    }
}

internal enum QUIC_CREDENTIAL_TYPE : int
{
    NONE = 0,
    CERTIFICATE_HASH = 1,
    CERTIFICATE_HASH_STORE = 2,
    CERTIFICATE_CONTEXT = 3,
    CERTIFICATE_FILE = 4,
    CERTIFICATE_FILE_PROTECTED = 5,
    CERTIFICATE_PKCS12 = 6,
}

[Flags]
internal enum QUIC_CREDENTIAL_FLAGS : int
{
    NONE = 0,
    CLIENT = 1,
    LOAD_ASYNCHRONOUS = 2,
    NO_CERTIFICATE_VALIDATION = 4,
    ENABLE_OCSP = 8,
    INDICATE_CERTIFICATE_RECEIVED = 16,
    DEFER_CERTIFICATE_VALIDATION = 32,
    REQUIRE_CLIENT_AUTHENTICATION = 64,
    USE_PORTABLE_CERTIFICATES = 16384,
}

[Flags]
internal enum QUIC_ALLOWED_CIPHER_SUITE_FLAGS : int
{
    NONE = 0,
}

internal enum QUIC_CONNECTION_SHUTDOWN_FLAGS : int
{
    NONE = 0,
    SILENT = 1,
}

[Flags]
internal enum QUIC_STREAM_OPEN_FLAGS : int
{
    NONE = 0,
    UNIDIRECTIONAL = 1,
}

[Flags]
internal enum QUIC_STREAM_START_FLAGS : int
{
    NONE = 0,
    IMMEDIATE = 1,
    FAIL_BLOCKED = 2,
    SHUTDOWN_ON_FAIL = 4,
    INDICATE_PEER_ACCEPT = 8,
    PRIORITY_WORK = 16,
}

[Flags]
internal enum QUIC_STREAM_SHUTDOWN_FLAGS : int
{
    NONE = 0,
    GRACEFUL = 1,
    ABORT_SEND = 2,
    ABORT_RECEIVE = 4,
    ABORT = 6,
    IMMEDIATE = 8,
}

[Flags]
internal enum QUIC_SEND_FLAGS : int
{
    NONE = 0,
    FIN = 4,
}

[Flags]
internal enum QUIC_RECEIVE_FLAGS : int
{
    NONE = 0,
    ZERO_RTT = 1,
    FIN = 2,
}

internal enum QUIC_SEND_RESUMPTION_FLAGS : int
{
    NONE = 0,
    FINAL = 1,
}

internal enum QUIC_TLS_ALERT_CODES : int
{
    BAD_CERTIFICATE = 42,
}

internal enum QUIC_DATAGRAM_SEND_STATE : int
{
    UNKNOWN = 0,
}

internal enum QUIC_LISTENER_EVENT_TYPE : int
{
    NEW_CONNECTION = 0,
    STOP_COMPLETE = 1,
    DOS_MODE_CHANGED = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_NEW_CONNECTION_INFO
{
    public uint QuicVersion;
    public QuicAddr* LocalAddress;
    public QuicAddr* RemoteAddress;
    public uint CryptoBufferLength;
    public ushort ClientAlpnListLength;
    public ushort ServerNameLength;
    public byte NegotiatedAlpnLength;
    public byte* CryptoBuffer;
    public byte* ClientAlpnList;
    public byte* NegotiatedAlpn;
    public sbyte* ServerName;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_LISTENER_EVENT
{
    public QUIC_LISTENER_EVENT_TYPE Type;
    public _Anonymous_e__Union Anonymous;

    [UnscopedRef]
    public ref _Anonymous_e__Union._NEW_CONNECTION_e__Struct NEW_CONNECTION => ref Anonymous.NEW_CONNECTION;

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct _Anonymous_e__Union
    {
        [FieldOffset(0)]
        public _NEW_CONNECTION_e__Struct NEW_CONNECTION;
        [FieldOffset(0)]
        public _STOP_COMPLETE_e__Struct STOP_COMPLETE;

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct _NEW_CONNECTION_e__Struct
        {
            public QUIC_NEW_CONNECTION_INFO* Info;
            public QUIC_HANDLE* Connection;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _STOP_COMPLETE_e__Struct
        {
            public byte Bitfield;
        }
    }
}

internal enum QUIC_CONNECTION_EVENT_TYPE : int
{
    CONNECTED = 0,
    SHUTDOWN_INITIATED_BY_TRANSPORT = 1,
    SHUTDOWN_INITIATED_BY_PEER = 2,
    SHUTDOWN_COMPLETE = 3,
    LOCAL_ADDRESS_CHANGED = 4,
    PEER_ADDRESS_CHANGED = 5,
    PEER_STREAM_STARTED = 6,
    STREAMS_AVAILABLE = 7,
    PEER_NEEDS_STREAMS = 8,
    IDEAL_PROCESSOR_CHANGED = 9,
    DATAGRAM_STATE_CHANGED = 10,
    DATAGRAM_RECEIVED = 11,
    DATAGRAM_SEND_STATE_CHANGED = 12,
    RESUMED = 13,
    RESUMPTION_TICKET_RECEIVED = 14,
    PEER_CERTIFICATE_RECEIVED = 15,
    RELIABLE_RESET_NEGOTIATED = 16,
    ONE_WAY_DELAY_NEGOTIATED = 17,
    NETWORK_STATISTICS = 18,
}

[StructLayout(LayoutKind.Sequential)]
internal struct QUIC_NETWORK_STATISTICS
{
    public uint BytesInFlight;
    public ulong PostedBytes;
    public ulong IdealBytes;
    public ulong SmoothedRTT;
    public uint CongestionWindow;
    public ulong Bandwidth;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_CONNECTION_EVENT
{
    public QUIC_CONNECTION_EVENT_TYPE Type;
    public _Anonymous_e__Union Anonymous;

    [UnscopedRef]
    public ref _Anonymous_e__Union._CONNECTED_e__Struct CONNECTED => ref Anonymous.CONNECTED;
    [UnscopedRef]
    public ref _Anonymous_e__Union._SHUTDOWN_INITIATED_BY_TRANSPORT_e__Struct SHUTDOWN_INITIATED_BY_TRANSPORT => ref Anonymous.SHUTDOWN_INITIATED_BY_TRANSPORT;
    [UnscopedRef]
    public ref _Anonymous_e__Union._SHUTDOWN_INITIATED_BY_PEER_e__Struct SHUTDOWN_INITIATED_BY_PEER => ref Anonymous.SHUTDOWN_INITIATED_BY_PEER;
    [UnscopedRef]
    public ref _Anonymous_e__Union._SHUTDOWN_COMPLETE_e__Struct SHUTDOWN_COMPLETE => ref Anonymous.SHUTDOWN_COMPLETE;
    [UnscopedRef]
    public ref _Anonymous_e__Union._PEER_ADDRESS_CHANGED_e__Struct PEER_ADDRESS_CHANGED => ref Anonymous.PEER_ADDRESS_CHANGED;
    [UnscopedRef]
    public ref _Anonymous_e__Union._PEER_STREAM_STARTED_e__Struct PEER_STREAM_STARTED => ref Anonymous.PEER_STREAM_STARTED;
    [UnscopedRef]
    public ref _Anonymous_e__Union._PEER_CERTIFICATE_RECEIVED_e__Struct PEER_CERTIFICATE_RECEIVED => ref Anonymous.PEER_CERTIFICATE_RECEIVED;

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct _Anonymous_e__Union
    {
        [FieldOffset(0)]
        public _CONNECTED_e__Struct CONNECTED;
        [FieldOffset(0)]
        public _SHUTDOWN_INITIATED_BY_TRANSPORT_e__Struct SHUTDOWN_INITIATED_BY_TRANSPORT;
        [FieldOffset(0)]
        public _SHUTDOWN_INITIATED_BY_PEER_e__Struct SHUTDOWN_INITIATED_BY_PEER;
        [FieldOffset(0)]
        public _SHUTDOWN_COMPLETE_e__Struct SHUTDOWN_COMPLETE;
        [FieldOffset(0)]
        public _LOCAL_ADDRESS_CHANGED_e__Struct LOCAL_ADDRESS_CHANGED;
        [FieldOffset(0)]
        public _PEER_ADDRESS_CHANGED_e__Struct PEER_ADDRESS_CHANGED;
        [FieldOffset(0)]
        public _PEER_STREAM_STARTED_e__Struct PEER_STREAM_STARTED;
        [FieldOffset(0)]
        public _PEER_CERTIFICATE_RECEIVED_e__Struct PEER_CERTIFICATE_RECEIVED;
        [FieldOffset(0)]
        public QUIC_NETWORK_STATISTICS NETWORK_STATISTICS;

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct _CONNECTED_e__Struct
        {
            public byte SessionResumed;
            public byte NegotiatedAlpnLength;
            public byte* NegotiatedAlpn;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _SHUTDOWN_INITIATED_BY_TRANSPORT_e__Struct
        {
            public int Status;
            public ulong ErrorCode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _SHUTDOWN_INITIATED_BY_PEER_e__Struct
        {
            public ulong ErrorCode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _SHUTDOWN_COMPLETE_e__Struct
        {
            public byte Bitfield;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct _LOCAL_ADDRESS_CHANGED_e__Struct
        {
            public QuicAddr* Address;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct _PEER_ADDRESS_CHANGED_e__Struct
        {
            public QuicAddr* Address;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct _PEER_STREAM_STARTED_e__Struct
        {
            public QUIC_HANDLE* Stream;
            public QUIC_STREAM_OPEN_FLAGS Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct _PEER_CERTIFICATE_RECEIVED_e__Struct
        {
            public void* Certificate;
            public uint DeferredErrorFlags;
            public int DeferredStatus;
            public void* Chain;
        }
    }
}

internal enum QUIC_STREAM_EVENT_TYPE : int
{
    START_COMPLETE = 0,
    RECEIVE = 1,
    SEND_COMPLETE = 2,
    PEER_SEND_SHUTDOWN = 3,
    PEER_SEND_ABORTED = 4,
    PEER_RECEIVE_ABORTED = 5,
    SEND_SHUTDOWN_COMPLETE = 6,
    SHUTDOWN_COMPLETE = 7,
    IDEAL_SEND_BUFFER_SIZE = 8,
    PEER_ACCEPTED = 9,
    CANCEL_ON_LOSS = 10,
    RECEIVE_BUFFER_NEEDED = 11,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_STREAM_EVENT
{
    public QUIC_STREAM_EVENT_TYPE Type;
    public _Anonymous_e__Union Anonymous;

    [UnscopedRef]
    public ref _Anonymous_e__Union._START_COMPLETE_e__Struct START_COMPLETE => ref Anonymous.START_COMPLETE;
    [UnscopedRef]
    public ref _Anonymous_e__Union._RECEIVE_e__Struct RECEIVE => ref Anonymous.RECEIVE;
    [UnscopedRef]
    public ref _Anonymous_e__Union._SEND_COMPLETE_e__Struct SEND_COMPLETE => ref Anonymous.SEND_COMPLETE;
    [UnscopedRef]
    public ref _Anonymous_e__Union._PEER_SEND_ABORTED_e__Struct PEER_SEND_ABORTED => ref Anonymous.PEER_SEND_ABORTED;
    [UnscopedRef]
    public ref _Anonymous_e__Union._PEER_RECEIVE_ABORTED_e__Struct PEER_RECEIVE_ABORTED => ref Anonymous.PEER_RECEIVE_ABORTED;
    [UnscopedRef]
    public ref _Anonymous_e__Union._SHUTDOWN_COMPLETE_e__Struct SHUTDOWN_COMPLETE => ref Anonymous.SHUTDOWN_COMPLETE;

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct _Anonymous_e__Union
    {
        [FieldOffset(0)]
        public _START_COMPLETE_e__Struct START_COMPLETE;
        [FieldOffset(0)]
        public _RECEIVE_e__Struct RECEIVE;
        [FieldOffset(0)]
        public _SEND_COMPLETE_e__Struct SEND_COMPLETE;
        [FieldOffset(0)]
        public _PEER_SEND_ABORTED_e__Struct PEER_SEND_ABORTED;
        [FieldOffset(0)]
        public _PEER_RECEIVE_ABORTED_e__Struct PEER_RECEIVE_ABORTED;
        [FieldOffset(0)]
        public _SEND_SHUTDOWN_COMPLETE_e__Struct SEND_SHUTDOWN_COMPLETE;
        [FieldOffset(0)]
        public _SHUTDOWN_COMPLETE_e__Struct SHUTDOWN_COMPLETE;
        [FieldOffset(0)]
        public _IDEAL_SEND_BUFFER_SIZE_e__Struct IDEAL_SEND_BUFFER_SIZE;
        [FieldOffset(0)]
        public _CANCEL_ON_LOSS_e__Struct CANCEL_ON_LOSS;
        [FieldOffset(0)]
        public _RECEIVE_BUFFER_NEEDED_e__Struct RECEIVE_BUFFER_NEEDED;

        [StructLayout(LayoutKind.Sequential)]
        internal struct _START_COMPLETE_e__Struct
        {
            public int Status;
            public ulong ID;
            public byte Bitfield;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct _RECEIVE_e__Struct
        {
            public ulong AbsoluteOffset;
            public ulong TotalBufferLength;
            public QUIC_BUFFER* Buffers;
            public uint BufferCount;
            public QUIC_RECEIVE_FLAGS Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct _SEND_COMPLETE_e__Struct
        {
            public byte Canceled;
            public void* ClientContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _PEER_SEND_ABORTED_e__Struct
        {
            public ulong ErrorCode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _PEER_RECEIVE_ABORTED_e__Struct
        {
            public ulong ErrorCode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _SEND_SHUTDOWN_COMPLETE_e__Struct
        {
            public byte Graceful;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _SHUTDOWN_COMPLETE_e__Struct
        {
            public byte ConnectionShutdown;
            public byte Bitfield;
            public ulong ConnectionErrorCode;
            public int ConnectionCloseStatus;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _IDEAL_SEND_BUFFER_SIZE_e__Struct
        {
            public ulong ByteCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _CANCEL_ON_LOSS_e__Struct
        {
            public ulong ErrorCode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct _RECEIVE_BUFFER_NEEDED_e__Struct
        {
            public ulong BufferLengthNeeded;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_SETTINGS
{
    public _Anonymous1_e__Union Anonymous1;
    public ulong MaxBytesPerKey;
    public ulong HandshakeIdleTimeoutMs;
    public ulong IdleTimeoutMs;
    public ulong MtuDiscoverySearchCompleteTimeoutUs;
    public uint TlsClientMaxSendBuffer;
    public uint TlsServerMaxSendBuffer;
    public uint StreamRecvWindowDefault;
    public uint StreamRecvBufferDefault;
    public uint ConnFlowControlWindow;
    public uint MaxWorkerQueueDelayUs;
    public uint MaxStatelessOperations;
    public uint InitialWindowPackets;
    public uint SendIdleTimeoutMs;
    public uint InitialRttMs;
    public uint MaxAckDelayMs;
    public uint DisconnectTimeoutMs;
    public uint KeepAliveIntervalMs;
    public ushort CongestionControlAlgorithm;
    public ushort PeerBidiStreamCount;
    public ushort PeerUnidiStreamCount;
    public ushort MaxBindingStatelessOperations;
    public ushort StatelessOperationExpirationMs;
    public ushort MinimumMtu;
    public ushort MaximumMtu;
    public byte _bitfield;
    public byte MaxOperationsPerDrain;
    public byte MtuDiscoveryMissingProbeCount;
    public uint DestCidUpdateIdleTimeoutMs;
    public _Anonymous2_e__Union Anonymous2;
    public uint StreamRecvWindowBidiLocalDefault;
    public uint StreamRecvWindowBidiRemoteDefault;
    public uint StreamRecvWindowUnidiDefault;

    [UnscopedRef]
    public ref _Anonymous1_e__Union._IsSet_e__Struct IsSet => ref Anonymous1.IsSet;

    [StructLayout(LayoutKind.Explicit)]
    internal struct _Anonymous1_e__Union
    {
        [FieldOffset(0)]
        public ulong IsSetFlags;
        [FieldOffset(0)]
        public _IsSet_e__Struct IsSet;

        [StructLayout(LayoutKind.Sequential)]
        internal struct _IsSet_e__Struct
        {
            public ulong _bitfield;

            public ulong IdleTimeoutMs
            {
                readonly get => (_bitfield >> 2) & 1UL;
                set => _bitfield = (_bitfield & ~(1UL << 2)) | ((value & 1UL) << 2);
            }

            public ulong KeepAliveIntervalMs
            {
                readonly get => (_bitfield >> 16) & 1UL;
                set => _bitfield = (_bitfield & ~(1UL << 16)) | ((value & 1UL) << 16);
            }

            public ulong PeerBidiStreamCount
            {
                readonly get => (_bitfield >> 18) & 1UL;
                set => _bitfield = (_bitfield & ~(1UL << 18)) | ((value & 1UL) << 18);
            }
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct _Anonymous2_e__Union
    {
        [FieldOffset(0)]
        public ulong Flags;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUIC_API_TABLE
{
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, void> SetContext;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*> GetContext;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, void*, void> SetCallbackHandler;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, uint, uint, void*, int> SetParam;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, uint, uint*, void*, int> GetParam;
    public delegate* unmanaged[Cdecl]<QUIC_REGISTRATION_CONFIG*, QUIC_HANDLE**, int> RegistrationOpen;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void> RegistrationClose;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_CONNECTION_SHUTDOWN_FLAGS, ulong, void> RegistrationShutdown;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_BUFFER*, uint, QUIC_SETTINGS*, uint, void*, QUIC_HANDLE**, int> ConfigurationOpen;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void> ConfigurationClose;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_CREDENTIAL_CONFIG*, int> ConfigurationLoadCredential;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, QUIC_LISTENER_EVENT*, int>, void*, QUIC_HANDLE**, int> ListenerOpen;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void> ListenerClose;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_BUFFER*, uint, QuicAddr*, int> ListenerStart;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void> ListenerStop;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, QUIC_CONNECTION_EVENT*, int>, void*, QUIC_HANDLE**, int> ConnectionOpen;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void> ConnectionClose;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_CONNECTION_SHUTDOWN_FLAGS, ulong, void> ConnectionShutdown;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_HANDLE*, ushort, sbyte*, ushort, int> ConnectionStart;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_HANDLE*, int> ConnectionSetConfiguration;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_SEND_RESUMPTION_FLAGS, ushort, byte*, int> ConnectionSendResumptionTicket;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_STREAM_OPEN_FLAGS, delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, QUIC_STREAM_EVENT*, int>, void*, QUIC_HANDLE**, int> StreamOpen;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void> StreamClose;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_STREAM_START_FLAGS, int> StreamStart;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_STREAM_SHUTDOWN_FLAGS, ulong, int> StreamShutdown;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_BUFFER*, uint, QUIC_SEND_FLAGS, void*, int> StreamSend;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, ulong, void> StreamReceiveComplete;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, byte, int> StreamReceiveSetEnabled;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, QUIC_BUFFER*, uint, QUIC_SEND_FLAGS, void*, int> DatagramSend;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, byte, int> ConnectionResumptionTicketValidationComplete;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, byte, QUIC_TLS_ALERT_CODES, int> ConnectionCertificateValidationComplete;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, ushort, delegate* unmanaged[Cdecl]<QUIC_HANDLE*, void*, QUIC_CONNECTION_EVENT*, int>, void*, QUIC_HANDLE**, int> ConnectionOpenInPartition;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, uint, QUIC_BUFFER*, int> StreamProvideReceiveBuffers;
    public delegate* unmanaged[Cdecl]<void*, QUIC_HANDLE**, int> ConnectionPoolCreate;
    public delegate* unmanaged[Cdecl]<int, uint, uint, void*, void**, int> ExecutionCreate;
    public delegate* unmanaged[Cdecl]<uint, void**, void> ExecutionDelete;
    public delegate* unmanaged[Cdecl]<void*, uint> ExecutionPoll;
    public delegate* unmanaged[Cdecl]<QUIC_HANDLE*, delegate* unmanaged[Cdecl]<void*, void>, void*, void> RegistrationClose2;
}

[StructLayout(LayoutKind.Explicit)]
internal struct QuicAddr
{
    [FieldOffset(0)]
    public QuicAddrIn Ipv4;
    [FieldOffset(0)]
    public QuicAddrIn6 Ipv6;
    [FieldOffset(0)]
    public QuicAddrFamilyAndLen FamilyLen;

    public ushort Family
    {
        readonly get => FamilyLen.sin_family;
        set => FamilyLen.sin_family = value;
    }
}

[StructLayout(LayoutKind.Explicit)]
internal struct QuicAddrFamilyAndLen
{
    [FieldOffset(0)]
    public ushort sin_family;
    [FieldOffset(0)]
    public byte sin_len;
    [FieldOffset(1)]
    public byte sin_family_bsd;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QuicAddrIn
{
    public QuicAddrFamilyAndLen sin_family;
    public ushort sin_port;
    public fixed byte sin_addr[4];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QuicAddrIn6
{
    public QuicAddrFamilyAndLen sin6_family;
    public ushort sin6_port;
    public uint sin6_flowinfo;
    public fixed byte sin6_addr[16];
    public uint sin6_scope_id;
}
