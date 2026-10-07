namespace DAL.Entities;

/// <summary>
/// Evidence attached to one answer of an assessment run (GitHub #80, T297, S44). The same
/// one-nullable-FK-per-attachment-target pattern as <see cref="RiskAcceptanceId"/> and
/// <see cref="IncidentId"/>.
/// </summary>
public partial class NrFile
{
    /// <summary>
    /// The answer row (<c>assessment_run_answers.id</c>) this file substantiates. Written only by the
    /// assessment evidence endpoints — the generic <c>/Files</c> write routes refuse it — so the run's
    /// submitted state, the size cap and the per-answer limit cannot be bypassed.
    /// </summary>
    public int? AssessmentRunAnswerId { get; set; }

    public AssessmentRunAnswer? AssessmentRunAnswer { get; set; }
}
