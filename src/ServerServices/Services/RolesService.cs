using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Interfaces;
using Tools.Helpers;

namespace ServerServices.Services;

public class RolesService(Serilog.ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IRolesService
{
    public List<Role> GetRoles()
    {
        using var dbContext = DalService.GetContext();
        var roles = dbContext.Roles.ToList();
        return roles;
    }

    public List<string> GetRolePermissions(int roleId)
    {
        return AsyncHelper.RunSync(async () => await GetRolePermissionsAsync(roleId));   
    }

    public async Task<List<string>> GetRolePermissionsAsync(int roleId)
    {
        await using var dbContext = DalService.GetContext();
        
        var role = dbContext.Roles
            .Include(r=> r.Permissions)
            .FirstOrDefault(r => r.Value == roleId);
        if(role == null) throw new Exception($"Role with id {roleId} not found");

        var permissions = role.Permissions;
        
        var result = new List<string>();
        
        Parallel.ForEach(permissions , permission =>
        {
            result.Add(permission.Key);
        });

        return result;
    }

    public Role? GetRole(int roleId)
    {
        return AsyncHelper.RunSync(async () => await GetRoleAsync(roleId));
    }

    public async Task<Role?> GetRoleAsync(int roleId)
    {
        await using var dbContext = DalService.GetContext();
        var role = await dbContext.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Value == roleId);
        
        if(role == null) throw new DataNotFoundException("netrisk", "role",new Exception($"Role with id {roleId} not found"));
        
        return role;
    }


    public void UpdatePermissions(int roleId, List<string> permissions)
    {
        AsyncHelper.RunSync(async () => await UpdatePermissionsAsync(roleId, permissions));
    }

    public async Task UpdatePermissionsAsync(int roleId, List<string> permissions)
    {
        await using var dbContext = DalService.GetContext();
        
        var role = await dbContext.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Value == roleId);
        
        if(role == null) throw new DataNotFoundException("netrisk", "role",new Exception($"Role with id {roleId} not found"));
        
        var permissionsToAdd = await dbContext.Permissions.AsAsyncEnumerable().Where(p => permissions.Contains(p.Key)).ToListAsync();
        
        role.Permissions.Clear();
        foreach (var permission in permissionsToAdd)
        {
            role.Permissions.Add(permission);
        }
        
        await dbContext.SaveChangesAsync();
    }

    public Role CreateRole(Role role)
    {
        role.Value = 0;
        using var dbContext = DalService.GetContext();
        var newRole = dbContext.Roles.Add(role);
        dbContext.SaveChanges();
        
        return newRole.Entity;
    }

    public void DeleteRole(int roleId)
    {
        
        using var dbContext = DalService.GetContext();
        var role = dbContext.Roles.Find(roleId);
        if (role == null) throw new DataNotFoundException("netrisk", "role", new Exception($"Role with id {roleId} not found"));
            
        dbContext.Roles.Remove(role);
        dbContext.SaveChanges();
    }
}