using DAL;
using DAL.Entities;
using Microsoft.Extensions.Logging;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class RoleManagementService: IRoleManagementService
{
    //private SRDbContext? _dbContext = null;
    private DALManager? _dalManager;
    private ILogger _log;

    public RoleManagementService(DALManager dalManager,
        ILoggerFactory logger )
    {
        _dalManager = dalManager;
        //_dbContext = dalManager.GetContext();
        _log = logger.CreateLogger(nameof(RoleManagementService));
    }


    public List<string> GetRolePermissions(int roleId)
    {
        var dbContext = _dalManager!.GetContext();
        var roles = dbContext!.RoleResponsibilities.Where(rlr => rlr.RoleId == roleId);

        var permissions = dbContext!.Permissions.Where(p => roles.Any(r => r.PermissionId == p.Id));

        var result = new List<string>();

        foreach (var permission in permissions)
        {
            result.Add(permission.Key);
        }

        return result;
    }

    public Role? GetRole(int roleId)
    {
        var dbContext = _dalManager!.GetContext();
        var role = dbContext!.Roles.Find(roleId);
        return role;
    }
}