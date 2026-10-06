namespace Model.Risks.Scenario;

/// <summary>
/// The body of <c>POST /Risks/ScenarioDuplicates</c> (Stage 9.2, T154, S42 §6): the (central event,
/// consequences) pair of a scenario about to be saved.
///
/// A POST rather than a query string: the two fields are free text of any length, and scenario text
/// does not belong in a URL that proxies and access logs record.
/// </summary>
public class RiskScenarioDuplicateQuery
{
    /// <summary>Required.</summary>
    public string? CentralEvent { get; set; }

    /// <summary>Required.</summary>
    public string? Consequences { get; set; }

    /// <summary>The risk being edited, so it is not reported as a duplicate of itself.</summary>
    public int? ExcludeRiskId { get; set; }
}
