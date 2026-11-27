using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Interfaces;
using Tools.Helpers;

namespace ServerServices.Services;

public class PermissionsService(
    IDalService dalService,
    IRolesService rolesService) : IPermissionsService
{
    private readonly IDalService? _dalService = dalService;

    public bool UserHasPermission(User user, string permission)
    {
        var permissions = GetUserPermissions(user);

        if (permissions.Contains(permission)) return true;
        
        return false;
    }

    public List<Permission> GetUserPermissionsById(int userId)
    {
        
        using var dbContext = _dalService!.GetContext();

        var user = dbContext.Users.Include(u=> u.Permissions).FirstOrDefault(u => u.Value == userId);
        
        if(user == null) throw new DataNotFoundException("user", userId.ToString());
        
        //var userPermissionsCon = dbContext.PermissionToUsers.Where(pu => pu.UserId == user.Value).ToList();
        
        //var userPermissions = dbContext.Permissions.Where(p => userPermissionsCon.Select(upc=> upc.PermissionId ).Contains(p.Id)).ToList();

        return user.Permissions.ToList();

    }

    public void SaveUserPermissionsById(int userId, List<int> permissions)
    {
        AsyncHelper.RunSync(async () => await SaveUserPermissionsByIdAsync(userId, permissions));
    }

    public async Task SaveUserPermissionsByIdAsync(int userId, List<int> permissions)
    {
        await using var dbContext = _dalService!.GetContext();
        
        var user = await dbContext.Users.Include(u=>u.Permissions).FirstOrDefaultAsync(u => u.Value == userId);
        
        if(user == null) throw new DataNotFoundException("user", userId.ToString());

        var up = await dbContext.Permissions.Where(p => permissions.Contains(p.Id)).ToListAsync();

        // A diff, not Clear() and re-add: an unchanged permission writes nothing, so the grant record sees real changes only.
        ApplyPermissionDiff(user.Permissions, up);

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Makes <paramref name="current"/> — a loaded, tracked permission collection — hold exactly <paramref name="desired"/>
    /// (tracked entities of the same context) by removing only what was dropped and adding only what is missing.
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    public static bool ApplyPermissionDiff(ICollection<Permission> current, IReadOnlyCollection<Permission> desired)
    {
        var desiredIds = desired.Select(p => p.Id).ToHashSet();
        var currentIds = current.Select(p => p.Id).ToHashSet();

        var dropped = current.Where(p => !desiredIds.Contains(p.Id)).ToList();
        foreach (var permission in dropped) current.Remove(permission);

        var added = 0;
        foreach (var permission in desired)
        {
            if (!currentIds.Add(permission.Id)) continue;
            current.Add(permission);
            added++;
        }

        return dropped.Count > 0 || added > 0;
    }
    
    public List<string> GetUserPermissions(User user)
    {
        return AsyncHelper.RunSync(async () => await GetUserPermissionsAsync(user));
    }

    public async Task<List<string>> GetUserPermissionsAsync(User user)
    {
        var permissions = new List<string>();

        if (user.RoleId > 0)
        {
            var rolePermissions = await rolesService.GetRolePermissionsAsync(user.RoleId);
            permissions.AddRange(rolePermissions);
        }
        
        await using var dbContext = _dalService!.GetContext();
        
        var dbuser = await dbContext.Users.Include(u=> u.Permissions).FirstOrDefaultAsync(u => u.Value == user.Value);
        
        if(dbuser == null) throw new DataNotFoundException("user", user.Value.ToString());
        
        var userPermissions = dbuser.Permissions.Select(p=>p.Key).ToList();

        // Sequential on purpose: this is a handful of strings, so Parallel.ForEach gained nothing, and
        // adding to a List<T> from several threads drops entries (an authorization check then wrongly
        // denies) or throws. The set keeps the old "role first, then the user's extras, no duplicates"
        // order without the quadratic Contains. One rule, shared with the authentication handlers' snapshot.
        return Security.PrincipalSnapshot.MergePermissions(permissions, userPermissions);
    }

    public List<Permission> GetAllPermissions()
    {
        using var dbContext = _dalService!.GetContext();
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
        using var dbContext = _dalService!.GetContext();
        var permission = dbContext.Permissions.FirstOrDefault(p => p.Key == permissionKey);
        if(permission == null) throw new DataNotFoundException("permission", permissionKey);

        return permission;
    }
}