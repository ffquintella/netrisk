using DAL.Entities;

namespace ServerServices.Interfaces;

/// <summary>
/// Who the business has appointed to review an entity's risks (Track 8 milestone 8.6.2).
///
/// The appointment is made in the desktop app by an entity administrator; the portal reads it to
/// decide what a reviewer may see. Keeping the two apart is the point of the design: the portal
/// never grants itself access.
/// </summary>
public interface IEntityRiskReviewersService
{
    Task<List<EntityRiskReviewer>> GetByEntityAsync(int entityId);

    /// <summary>The entities a user is appointed to review. The portal's whole authorization model.</summary>
    Task<List<int>> GetEntitiesForReviewerAsync(int userId);

    Task<EntityRiskReviewer> AppointAsync(int entityId, int userId, bool isPrimary, int actingUserId);

    Task RemoveAsync(int id);
}
