using DAL;
using DAL.Entities;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class PermissionsService: IPermissionsService
{
    private DALManager? _dalManager;
    private IRolesService _rolesService;
    public PermissionsService(DALManager dalManager,
        IRolesService rolesService)
    {
        _dalManager = dalManager;
        _rolesService = rolesService;
    }
    public bool UserHasPermission(User user, string permission)
    {
        var permissions = GetUserPermissions(user);

        if (permissions.Contains(permission)) return true;
        
        return false;
    }
    
    public List<string> GetUserPermissions(User user)
    {

        var permissions = new List<string>();

        if (user.RoleId > 0)
        {
            var rolePermissions = _rolesService.GetRolePermissions(user.RoleId);
            permissions = rolePermissions;
        }
        
        var dbContext = _dalManager!.GetContext();
        
        var userPermissionsCon = dbContext!.PermissionToUsers.Where(pu => pu.UserId == user.Value).ToList();
        
        var userPermissions = dbContext!.Permissions.Where(p => userPermissionsCon.Select(upc=> upc.PermissionId ).Contains(p.Id)).ToList();

        var strUserPermissions = userPermissions.Select(up=>up.Key).ToList();

        var finalPermissions = permissions.Concat(strUserPermissions).ToList();
        
        return finalPermissions;
    }
}