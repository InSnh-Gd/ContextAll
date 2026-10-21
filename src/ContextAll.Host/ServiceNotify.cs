using System.Net.Sockets;
using System.Text;

namespace ContextAll.Host;

public static class ServiceNotify
{
    public static void Ready()
    {
        var path = Environment.GetEnvironmentVariable("NOTIFY_SOCKET");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        using var socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
        var endpoint = path[0] == '@'
            ? new UnixDomainSocketEndPoint("\0" + path[1..])
            : new UnixDomainSocketEndPoint(path);
        socket.Connect(endpoint);
        socket.Send(Encoding.ASCII.GetBytes("READY=1"));
    }
}
