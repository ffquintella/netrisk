using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Interfaces;

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
        using var dbContext = DalService.GetContext();


        var role = dbContext.Roles
            .Include(r=> r.Permissions)
            .FirstOrDefault(r => r.Value == roleId);
        if(role == null) throw new Exception($"Role with id {roleId} not found");

        var permissions = role.Permissions;
        
        var result = new List<string>();

        foreach (var permission in permissions)
        {
            result.Add(permission.Key);
        }

        return result;
        
        
    }

    public Role GetRole(int roleId)
    {
        using var dbContext = DalService.GetContext();
        var role = dbContext.Roles
            .Include(r => r.Permissions)
            .FirstOrDefault(r => r.Value == roleId);
        
        if(role == null) throw new DataNotFoundException("netrisk", "role",new Exception($"Role with id {roleId} not found"));
        
        return role;
    }


    public void UpdatePermissions(int roleId, List<string> permissions)
    {
        using var dbContext = DalService.GetContext();
        
        var role = dbContext.Roles
            .Include(r => r.Permissions)
            .FirstOrDefault(r => r.Value == roleId);
        
        if(role == null) throw new DataNotFoundException("netrisk", "role",new Exception($"Role with id {roleId} not found"));
        
        var permissionsToAdd = dbContext.Permissions.Where(p => permissions.Contains(p.Key)).ToList();
        
        role.Permissions.Clear();
        foreach (var permission in permissionsToAdd)
        {
            role.Permissions.Add(permission);
        }
        
        dbContext.SaveChanges();
        
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