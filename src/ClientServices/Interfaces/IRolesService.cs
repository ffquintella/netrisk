using DAL.Entities;

namespace ClientServices.Interfaces;

public interface IRolesService
{
    /// <summary>
    /// Gets all roles
    /// </summary>
    /// <returns></returns>
    public List<Role> GetAllRoles();
}