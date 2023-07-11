using DAL;
using DAL.Entities;
using Model.Exceptions;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class PermissionsService: IPermissionsService
{
    private readonly DALManager? _dalManager;
    private readonly IRolesService _rolesService;
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

    public List<Permission> GetUserPermissionsById(int userId)
    {
        
        using var dbContext = _dalManager!.GetContext();

        var user = dbContext.Users.FirstOrDefault(u => u.Value == userId);
        
        if(user == null) throw new DataNotFoundException("user", userId.ToString());
        
        var userPermissionsCon = dbContext.PermissionToUsers.Where(pu => pu.UserId == user.Value).ToList();
        
        var userPermissions = dbContext.Permissions.Where(p => userPermissionsCon.Select(upc=> upc.PermissionId ).Contains(p.Id)).ToList();

        return userPermissions;

    }
    
    public List<string> GetUserPermissions(User user)
    {

        var permissions = new List<string>();

        if (user.RoleId > 0)
        {
            var rolePermissions = _rolesService.GetRolePermissions(user.RoleId);
            permissions = rolePermissions;
        }
        
        using var dbContext = _dalManager!.GetContext();
        
        var userPermissionsCon = dbContext.PermissionToUsers.Where(pu => pu.UserId == user.Value).ToList();
        
        var userPermissions = dbContext.Permissions.Where(p => userPermissionsCon.Select(upc=> upc.PermissionId ).Contains(p.Id)).ToList();

        var strUserPermissions = userPermissions.Select(up=>up.Key).ToList();

        var finalPermissions = permissions.Concat(strUserPermissions).ToList();
        
        return finalPermissions;
    }

    public List<Permission> GetAllPermissions()
    {
        using var dbContext = _dalManager!.GetContext();
        var permissions = dbContext.Permissions.ToList();

        return permissions;
    }
}