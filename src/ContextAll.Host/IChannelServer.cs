using ContextAll.Ipc;

namespace ContextAll.Host;

public interface IChannelServer
{
    LocalTransport Transport { get; }

    Task<int> ServeAsync(
        Func<LanHost?> hostFactory,
        Func<CancellationToken, Task>? onListening,
        CancellationToken cancellationToken);
}

public sealed class UnixChannelServer : IChannelServer
{
    readonly string? runtimeDirectory;

    public UnixChannelServer(string? runtimeDirectory)
    {
        this.runtimeDirectory = runtimeDirectory;
    }

    public LocalTransport Transport => LocalTransport.UnixSocket;

    public Task<int> ServeAsync(
        Func<LanHost?> hostFactory,
        Func<CancellationToken, Task>? onListening,
        CancellationToken cancellationToken) =>
        DaemonProcess.Serve(runtimeDirectory, hostFactory, onListening, cancellationToken);
}

public sealed class PipeChannelServer : IChannelServer
{
    readonly string userSid;

    public PipeChannelServer(string userSid)
    {
        this.userSid = userSid;
    }

    public LocalTransport Transport => LocalTransport.NamedPipe;

    public Task<int> ServeAsync(
        Func<LanHost?> hostFactory,
        Func<CancellationToken, Task>? onListening,
        CancellationToken cancellationToken) =>
        DaemonProcess.ServePipe(userSid, hostFactory, onListening, cancellationToken);
}

public static class ChannelServers
{
    public static IChannelServer ForThisPlatform(string? runtimeDirectory) =>
        ForPlatform(OperatingSystem.IsWindows(), runtimeDirectory, LocalChannelClient.CurrentUserSid());

    public static IChannelServer ForPlatform(bool windows, string? runtimeDirectory, string userSid) =>
        windows ? new PipeChannelServer(userSid) : new UnixChannelServer(runtimeDirectory);
}
