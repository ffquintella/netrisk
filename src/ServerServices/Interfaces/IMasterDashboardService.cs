using System.Threading.Tasks;
using Model.Dashboard;

namespace ServerServices.Interfaces;

/// <summary>
/// Cross-entity posture aggregation for the administrator Master Dashboard
/// (Track 2 milestone 2.3.3).
/// </summary>
public interface IMasterDashboardService
{
    /// <summary>
    /// Computes one rollup per business entity plus organisation-wide totals.
    /// </summary>
    /// <param name="useCache">
    /// When true (the default) a result younger than the service's cache window is reused.
    /// Pass false to force a recompute — the GUI's explicit Refresh does this.
    /// </param>
    Task<MasterDashboard> GetMasterDashboardAsync(bool useCache = true);
}
