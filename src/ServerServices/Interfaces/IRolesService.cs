using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IRolesService
{
    List<string> GetRolePermissions(int roleId);
    
    Role? GetRole(int roleId);
}