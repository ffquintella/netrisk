using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IPermissionsService
{
    bool UserHasPermission(User user, string Permission);
    List<string> GetUserPermissions(User user);
}