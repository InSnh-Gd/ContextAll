using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ContextAll.Host;

public static class PipeAcl
{
    [SupportedOSPlatform("windows")]
    public static PipeSecurity CurrentUserOnly(string userSid)
    {
        var security = new PipeSecurity();
        var user = new SecurityIdentifier(userSid);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        var world = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new PipeAccessRule(world, PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }
}
