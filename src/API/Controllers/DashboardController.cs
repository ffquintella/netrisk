using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Dashboard;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Cross-entity posture aggregates (Track 2 milestone 2.3.3).
/// </summary>
[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class DashboardController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IMasterDashboardService masterDashboardService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>
    /// The administrator Master Dashboard: one posture rollup per business entity plus
    /// organisation-wide totals, computed in a single server-side pass.
    /// </summary>
    /// <param name="refresh">Bypass the short server-side cache and recompute.</param>
    [HttpGet]
    [Route("Master")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MasterDashboard))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MasterDashboard>> GetMasterDashboard([FromQuery] bool refresh = false)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} requested the master dashboard (refresh:{Refresh})", user.Value, refresh);

        var dashboard = await masterDashboardService.GetMasterDashboardAsync(useCache: !refresh);

        return Ok(dashboard);
    }
}
