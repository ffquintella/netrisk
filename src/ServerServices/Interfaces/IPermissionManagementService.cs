using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IPermissionManagementService
{
    bool UserHasPermission(User user, string Permission);
    List<string> GetUserPermissions(User user);
}