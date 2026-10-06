namespace Model.Risks.Scenario;

/// <summary>
/// An existing risk whose (central event, consequences) pair matches the one being saved (Stage 9.2,
/// T154, S42 §6). A warning, never a refusal: the caller decides whether it is the same scenario.
/// </summary>
public class RiskScenarioDuplicate
{
    public int RiskId { get; set; }

    public string Subject { get; set; } = string.Empty;

    /// <summary>Any status, closed included — a closed risk with the same scenario is worth knowing about.</summary>
    public string Status { get; set; } = string.Empty;

    public string? CentralEvent { get; set; }

    public string? Consequences { get; set; }
}
