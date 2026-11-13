using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ContextAll.Core;

namespace ContextAll.Host;

public static class LocalNetwork
{
    public static List<OnLinkPrefix> Enumerate()
    {
        var prefixes = new List<OnLinkPrefix>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsEligible(nic.OperationalStatus))
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var index = InterfaceIndex(nic);
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (!IsUsableAddress(unicast.Address))
                {
                    continue;
                }

                prefixes.Add(new OnLinkPrefix(unicast.Address.ToString(), unicast.PrefixLength)
                {
                    InterfaceIndex = index,
                    LinkLocal = IsLinkLocal(unicast.Address),
                });
            }
        }

        return prefixes;
    }

    internal static bool IsEligible(OperationalStatus status) => status == OperationalStatus.Up;

    internal static bool IsUsableAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            return false;
        }

        return !address.IsIPv6Multicast;
    }

    internal static string Text(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.ToString();
    }

    internal static bool IsLinkLocal(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 169 && bytes[1] == 254;
        }

        return address.IsIPv6LinkLocal;
    }

    static int InterfaceIndex(NetworkInterface nic)
    {
        try
        {
            return nic.GetIPProperties().GetIPv6Properties().Index;
        }
        catch (NetworkInformationException)
        {
            // Interfaces without an IPv6 stack fall through to the IPv4 index.
        }

        try
        {
            return nic.GetIPProperties().GetIPv4Properties().Index;
        }
        catch (NetworkInformationException)
        {
            return 0;
        }
    }
}
