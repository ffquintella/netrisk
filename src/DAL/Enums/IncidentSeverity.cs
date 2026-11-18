namespace DAL.Enums;

/// <summary>
/// How severe an incident is (M58, T279; S39 §3.1, D-15), stored in <c>incidents.severity</c> as its integer value.
/// The member names, lower-cased, are the keys of the risk index's <c>ancoras_incidente_severidade</c>
/// (<c>RiskIndexParameters.IncidentSeverityAnchors</c>): when an incident has one, it replaces the category anchor.
/// Null on the incident means "not assessed", never "low".
/// </summary>
public enum IncidentSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
