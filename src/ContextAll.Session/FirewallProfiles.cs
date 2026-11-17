using ContextAll.Core;
using ContextAll.Session.Abstractions;

namespace ContextAll.Session;

public static class FirewallProfiles
{
    public static bool AllowsLan(FirewallProfileKind kind) =>
        kind is FirewallProfileKind.Private or FirewallProfileKind.Domain;

    public static IEnumerable<OnLinkPrefix> Filter(IEnumerable<OnLinkPrefix> prefixes, IFirewallProfile? firewall)
    {
        if (firewall is null)
        {
            return prefixes;
        }

        return prefixes.Where(prefix =>
            !string.IsNullOrEmpty(prefix.InterfaceId) && AllowsLan(firewall.ProfileFor(prefix.InterfaceId)));
    }
}

public sealed class CachingFirewallProfile : IFirewallProfile, IDisposable
{
    readonly Func<string, FirewallProfileKind> lookup;
    readonly Dictionary<string, FirewallProfileKind> cache = new(StringComparer.OrdinalIgnoreCase);
    readonly object gate = new();
    bool disposed;

    public CachingFirewallProfile(Func<string, FirewallProfileKind> lookup)
    {
        this.lookup = lookup;
    }

    public event EventHandler? Changed;

    public FirewallProfileKind ProfileFor(string interfaceId)
    {
        lock (gate)
        {
            if (cache.TryGetValue(interfaceId, out var cached))
            {
                return cached;
            }
        }

        FirewallProfileKind kind;
        try
        {
            kind = lookup(interfaceId);
        }
        catch (Exception)
        {
            kind = FirewallProfileKind.Unknown;
        }

        lock (gate)
        {
            cache[interfaceId] = kind;
        }

        return kind;
    }

    public void Invalidate()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            cache.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            cache.Clear();
        }
    }
}
