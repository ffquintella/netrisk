using Microsoft.AspNetCore.Authorization;

namespace API.Security;

public class PermissionAuthorizeAttribute: AuthorizeAttribute
{
    const string PolicyPrefix = "Permission";

    public PermissionAuthorizeAttribute(string permission) => Permission = permission;

    // Get or set the Age property by manipulating the underlying Policy property
    public string Permission
    {
        get
        {

            var permission = Policy!.Substring(PolicyPrefix.Length);
            return permission;
            
        }
        set => Policy = $"{PolicyPrefix}{value}";
    }
}