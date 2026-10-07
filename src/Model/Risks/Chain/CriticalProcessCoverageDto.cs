namespace Model.Risks.Chain;

/// <summary>
/// The Phase 7 "critical-process coverage" metric (Stage 9.1, S41 §6):
/// <c>GET /RiskChain/Coverage/CriticalProcesses</c>.
///
/// Only active processes whose effective criticality (BIA MTPD first, S43) is 4 or 5 enter the denominator. A
/// process with no criticality, or one outside 1–5, is counted in
/// <see cref="ProcessesWithoutCriticality"/> — visible, and not silently "not critical". A process is
/// covered when at least one open risk is linked to it directly or by inference.
/// </summary>
public class CriticalProcessCoverageDto
{
    /// <summary>UTC.</summary>
    public DateTime ComputedAt { get; set; }

    /// <summary>The criticality from which a process counts as critical: 4.</summary>
    public int Threshold { get; set; }

    public int CriticalProcessCount { get; set; }

    public int CoveredCount { get; set; }

    /// <summary>
    /// <see cref="CoveredCount"/> / <see cref="CriticalProcessCount"/>, unrounded. Null when there is no
    /// critical process — "not computable", which is neither 0 % nor 100 %.
    /// </summary>
    public decimal? CoverageRatio { get; set; }

    /// <summary>Active processes whose criticality is absent or outside 1–5.</summary>
    public int ProcessesWithoutCriticality { get; set; }

    /// <summary>
    /// True when the caller's entity scope is restricted, so the counts cover only the risks the
    /// caller can see and the result is partial.
    /// </summary>
    public bool IsScopeRestricted { get; set; }

    public List<CriticalProcessCoverageRowDto> Rows { get; set; } = new();
}

/// <summary>One critical process and how it is covered.</summary>
public class CriticalProcessCoverageRowDto
{
    public int ProcessId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public int Criticality { get; set; }

    /// <summary>
    /// Where <see cref="Criticality"/> comes from (Stage 9.3, S43 §4.6): the BIA's MTPD when declared,
    /// which wins, or the declared entity property.
    /// </summary>
    public Model.Continuity.CriticalitySource CriticalitySource { get; set; } = Model.Continuity.CriticalitySource.Declared;

    /// <summary>Open risks linked to the process itself.</summary>
    public int DirectOpenRiskCount { get; set; }

    /// <summary>
    /// Open risks linked only to a node below the process — a service that serves it, an application
    /// it or its service uses, the data its service handles, one of its activities. A risk linked to
    /// the process and to a node below it is counted once, as direct.
    /// </summary>
    public int InferredOpenRiskCount { get; set; }

    public bool Covered { get; set; }
}
