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
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var user = new SecurityIdentifier(userSid);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
