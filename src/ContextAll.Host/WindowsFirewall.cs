using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ContextAll.Session;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

public static class WindowsFirewall
{
    public static IFirewallProfile? ForThisPlatform() =>
        OperatingSystem.IsWindows() ? CreateWindows() : null;

    [SupportedOSPlatform("windows")]
    static IFirewallProfile CreateWindows() =>
        new NetworkAwareFirewallProfile(NetworkListLookup.ProfileForAdapter);
}

public sealed class NetworkAwareFirewallProfile : IFirewallProfile, IDisposable
{
    readonly CachingFirewallProfile inner;

    public NetworkAwareFirewallProfile(Func<string, FirewallProfileKind> lookup)
    {
        inner = new CachingFirewallProfile(lookup);
        inner.Changed += OnInnerChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    public event EventHandler? Changed;

    public FirewallProfileKind ProfileFor(string interfaceId) => inner.ProfileFor(interfaceId);

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        inner.Changed -= OnInnerChanged;
        inner.Dispose();
    }

    void OnInnerChanged(object? sender, EventArgs args) => Changed?.Invoke(this, args);

    void OnNetworkAddressChanged(object? sender, EventArgs args) => inner.Invalidate();

    void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => inner.Invalidate();
}

static class NetworkListLookup
{
    static readonly Guid ClsidNetworkListManager = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    public static FirewallProfileKind ProfileForAdapter(string adapterId)
    {
        if (!OperatingSystem.IsWindows() || !Guid.TryParse(adapterId, out var adapter))
        {
            return FirewallProfileKind.Unknown;
        }

        return LookupWindows(adapter);
    }

    [SupportedOSPlatform("windows")]
    static FirewallProfileKind LookupWindows(Guid adapter)
    {
        var manager = CreateManager();
        if (manager is null)
        {
            return FirewallProfileKind.Unknown;
        }

        try
        {
            var connections = manager.GetNetworkConnections() as IEnumerable;
            if (connections is null)
            {
                return FirewallProfileKind.Unknown;
            }

            foreach (var item in connections)
            {
                if (item is not INetworkConnection connection || connection.GetAdapterId() != adapter)
                {
                    continue;
                }

                var network = connection.GetNetwork();
                return network.GetCategory() switch
                {
                    NlmNetworkCategory.Private => FirewallProfileKind.Private,
                    NlmNetworkCategory.DomainAuthenticated => FirewallProfileKind.Domain,
                    NlmNetworkCategory.Public => FirewallProfileKind.Public,
                    _ => FirewallProfileKind.Unknown,
                };
            }
        }
        catch (COMException)
        {
            return FirewallProfileKind.Unknown;
        }
        catch (InvalidCastException)
        {
            return FirewallProfileKind.Unknown;
        }

        return FirewallProfileKind.Unknown;
    }

    [SupportedOSPlatform("windows")]
    [UnconditionalSuppressMessage("TrimAnalyzer", "IL2072", Justification = "Network List Manager is a COM coclass created only on Windows.")]
    static INetworkListManager? CreateManager()
    {
        var type = Type.GetTypeFromCLSID(ClsidNetworkListManager, throwOnError: false);
        if (type is null)
        {
            return null;
        }

        var created = Activator.CreateInstance(type);
        return created as INetworkListManager;
    }
}

enum NlmNetworkCategory
{
    Public = 0,
    Private = 1,
    DomainAuthenticated = 2,
}

[ComImport]
[Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
interface INetworkListManager
{
    [return: MarshalAs(UnmanagedType.IDispatch)]
    object GetNetworkConnections();
}

[ComImport]
[Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
interface INetworkConnection
{
    INetwork GetNetwork();
    short get_IsConnectedToInternet();
    short get_IsConnected();
    int GetConnectivity();
    Guid GetConnectionId();
    Guid GetAdapterId();
}

[ComImport]
[Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
interface INetwork
{
    NlmNetworkCategory GetCategory();
}
