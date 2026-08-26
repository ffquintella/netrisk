using DAL.Entities;

namespace ServerServices.Interfaces;

/// <summary>
/// Administration of the risk appetite thresholds (Track 8 milestone 8.3.3).
///
/// Reading and evaluating an appetite is <see cref="IRiskWorkflowService"/>'s job; this is the
/// narrow CRUD surface the admin screen drives, kept separate so the gate cannot be changed by
/// anything that merely consults it.
/// </summary>
public interface IRiskAppetitesService
{
    Task<List<RiskAppetite>> GetAllAsync();

    Task<RiskAppetite?> GetGlobalAsync();

    Task<RiskAppetite> SaveAsync(RiskAppetite appetite, int actingUserId);

    Task DeleteAsync(int id);
}
