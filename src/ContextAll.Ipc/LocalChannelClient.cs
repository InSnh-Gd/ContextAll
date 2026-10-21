using ContextAll.Protocol;

namespace ContextAll.Ipc;

public enum LocalTransport
{
    UnixSocket,
    NamedPipe,
}

public sealed class LocalChannelClient
{
    public LocalChannelClient(LocalTransport transport)
    {
        Transport = transport;
    }

    public LocalTransport Transport { get; }

    public static string LinuxSocket(string runtimeDirectory) =>
        Path.Combine(runtimeDirectory, "contextall", "daemon.sock");

    public static string WindowsPipe(string userSid) => $@"\\.\pipe\ContextAll\{userSid}";

    public static LocalChannelClient ForThisPlatform() =>
        OperatingSystem.IsWindows()
            ? new LocalChannelClient(LocalTransport.NamedPipe)
            : new LocalChannelClient(LocalTransport.UnixSocket);
}
