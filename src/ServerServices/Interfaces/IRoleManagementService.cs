using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IRoleManagementService
{
    List<string> GetRolePermissions(int roleId);
    
    Role? GetRole(int roleId);
}