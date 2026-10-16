namespace DAL.Entities;

public partial class NrFile
{
    /// <summary>
    /// The IT Risk Committee's minutes attached to an index profile's approval (S40 §6.4, §7.3; T262), the
    /// one-nullable-FK-per-attachment-target pattern of <see cref="RiskAcceptanceId"/>. Linked only by
    /// <c>POST /RiskIndex/Profiles/{id}/Attachments</c>, never by the generic files routes. No navigation: the entity map
    /// of the index stays inside the index's own types (<c>IcrIsNotADecisionInputTest</c>).
    /// </summary>
    public int? RiskIndexProfileId { get; set; }
}
