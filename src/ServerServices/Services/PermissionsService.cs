using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
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

        var user = dbContext.Users.Include(u=> u.Permissions).FirstOrDefault(u => u.Value == userId);
        
        if(user == null) throw new DataNotFoundException("user", userId.ToString());
        
        //var userPermissionsCon = dbContext.PermissionToUsers.Where(pu => pu.UserId == user.Value).ToList();
        
        //var userPermissions = dbContext.Permissions.Where(p => userPermissionsCon.Select(upc=> upc.PermissionId ).Contains(p.Id)).ToList();

        return user.Permissions.ToList();

    }

    public void SaveUserPermissionsById(int userId, List<int> permissions)
    {
        using var dbContext = _dalManager!.GetContext();
        var user = dbContext.Users.Include(u=>u.Permissions).FirstOrDefault(u => u.Value == userId);
        
        if(user == null) throw new DataNotFoundException("user", userId.ToString());

        var up  = dbContext.Permissions.Where(p => permissions.Contains(p.Id)).ToList();

        user.Permissions.Clear();
       
        user.Permissions = up;
        
        dbContext.SaveChanges();
        
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
        
        /*var userPermissionsCon = dbContext.PermissionToUsers.Where(pu => pu.UserId == user.Value).ToList();
        
        var userPermissions = dbContext.Permissions.Where(p => userPermissionsCon.Select(upc=> upc.PermissionId ).Contains(p.Id)).ToList();

        var strUserPermissions = userPermissions.Select(up=>up.Key).ToList();

        var finalPermissions = permissions.Concat(strUserPermissions).ToList();*/
        
        var dbuser = dbContext.Users.Include(u=> u.Permissions).FirstOrDefault(u => u.Value == user.Value);
        
        if(dbuser == null) throw new DataNotFoundException("user", user.Value.ToString());
        
        var userPermissions = dbuser.Permissions.Select(p=>p.Key).ToList();

        permissions.AddRange(userPermissions);
        
        return permissions;
    }

    public List<Permission> GetAllPermissions()
    {
        using var dbContext = _dalManager!.GetContext();
        var permissions = dbContext.Permissions.ToList();

        return permissions;
    }

    public List<Permission> GetDefaultPermissions()
    {
        var result = new List<Permission>();

        result.Add(GetByKey("submit_risks"));
        result.Add(GetByKey("reports"));
        
        
        return result;
    }

    public Permission GetByKey(string permissionKey)
    {
        using var dbContext = _dalManager!.GetContext();
        var permission = dbContext.Permissions.FirstOrDefault(p => p.Key == permissionKey);
        if(permission == null) throw new DataNotFoundException("permission", permissionKey);

        return permission;
    }
}