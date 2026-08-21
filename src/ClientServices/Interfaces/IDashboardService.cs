using System.Threading.Tasks;
using Model.Dashboard;

namespace ClientServices.Interfaces;

/// <summary>
/// Client access to the cross-entity Master Dashboard (Track 2 milestone 2.3.3).
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Fetches the administrator Master Dashboard. The endpoint is admin-only; a non-admin
    /// caller gets a 403, surfaced here as <see cref="Model.Exceptions.InvalidHttpRequestException"/>.
    /// </summary>
    /// <param name="refresh">Ask the server to bypass its short cache and recompute.</param>
    Task<MasterDashboard> GetMasterDashboardAsync(bool refresh = false);
}
