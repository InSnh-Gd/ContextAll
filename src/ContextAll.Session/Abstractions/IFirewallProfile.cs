namespace ContextAll.Session.Abstractions;

public enum FirewallProfileKind
{
    Public,
    Private,
    Domain,
    Unknown,
}

public interface IFirewallProfile
{
    FirewallProfileKind ProfileFor(string interfaceId);

    event EventHandler? Changed;
}
