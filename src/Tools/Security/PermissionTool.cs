using DAL.Entities;
using Model.Authentication;

namespace Tools.Security;

public static class PermissionTool
{
    public static bool VerifyPermission(string permission, DAL.Entities.User user)
    {
        if(user.Admin) return true;
        
        if(user.Permissions.FirstOrDefault(p => p.Key == permission) != null) return true;

        return false;
    }
    
    public static bool VerifyPermission(string permission, AuthenticatedUserInfo? user)
    {

        if (user == null) return false;
        if(user.IsAdmin) return true;
        
        if(user.UserPermissions == null) return false;
        if(user.UserPermissions.Contains(permission)) return true;

        return false;
    }
}