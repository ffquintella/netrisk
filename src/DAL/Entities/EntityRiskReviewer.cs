namespace DAL.Entities;

/// <summary>
/// A business-appointed risk reviewer for one entity (Track 8 milestone 8.6.2).
///
/// This is the "first line decides" half of the three-lines model: the reviewer is not a security
/// analyst, they are the person the business names as accountable for its risks, and the portal is
/// the only surface they need. Appointed by an entity admin from the desktop app.
/// </summary>
public class EntityRiskReviewer : DAL.Interfaces.IEntityScoped
{
    public int Id { get; set; }

    public int EntityId { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// The reviewer campaign notifications address first. Several reviewers may share an entity;
    /// exactly one being primary is what keeps "who is chased when the campaign is overdue" from
    /// being a judgement call.
    /// </summary>
    public bool IsPrimary { get; set; }

    public int? AppointedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual User? User { get; set; }

    public virtual User? AppointedBy { get; set; }

    int? DAL.Interfaces.IEntityScoped.EntityId
    {
        // 0 reads as "not set yet" so a single-entity caller gets the row filed automatically by
        // AuditableContext, exactly as it does for the entities whose column is genuinely nullable.
        get => EntityId == 0 ? null : EntityId;
        set => EntityId = value ?? 0;
    }
}
