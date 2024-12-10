using DAL.Entities;

namespace Tools.Security;

public static class PermissionTool
{
    public static bool VerifyPermission(string permission, DAL.Entities.User user)
    {
        if(user.Admin) return true;
        
        if(user.Permissions.FirstOrDefault(p => p.Key == permission) != null) return true;

        return false;
    }
}