using Microsoft.AspNetCore.Authorization;

namespace API.Security;

public class PermissionAuthorizeAttribute: AuthorizeAttribute
{
    const string POLICY_PREFIX = "Permission";

    public PermissionAuthorizeAttribute(string permission) => Permission = permission;

    // Get or set the Age property by manipulating the underlying Policy property
    public string Permission
    {
        get
        {

            var permission = Policy.Substring(POLICY_PREFIX.Length);
            return permission;
            
        }
        set
        {
            Policy = $"{POLICY_PREFIX}{value}";
        }
    }
}