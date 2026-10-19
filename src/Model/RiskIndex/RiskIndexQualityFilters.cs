using System.Collections.Generic;

namespace Model.RiskIndex;

/// <summary>
/// The <c>chip=</c> filters of <c>GET /RiskIndex/Objects</c> that the data-quality items open (S40 §5.12, §8.2; T256). Shared
/// by the server's filter and the desktop's links, so the two cannot drift apart.
/// </summary>
public static class RiskIndexQualityFilters
{
    public const string Key = "chip";

    /// <summary>Rows whose criticality came from the profile's default (<c>criticality_source = default</c>).</summary>
    public const string DefaultCriticality = "default_criticality";

    /// <summary>Presumed rows of critical objects (criticality ≥ <see cref="CriticalFrom"/>, S39 §8.6).</summary>
    public const string PresumedCritical = "presumed_critical";

    /// <summary>Register rows whose stored residual is above the inherent (S39 §3.3, D-04).</summary>
    public const string ResidualAboveInherent = "residual_above_inherent";

    /// <summary>The criticality source the collector writes when nothing declared one.</summary>
    public const string DefaultCriticalitySource = "default";

    /// <summary>"Ativos críticos": criticality 4 and 5.</summary>
    public const int CriticalFrom = 4;

    /// <summary>The evidence confidence of M40 is shown apart from quality and is not built yet (S39 §8.5).</summary>
    public const string EvidenceConfidenceRequiresM40 = "requires_m40";

    public static IReadOnlyList<string> Values { get; } = [DefaultCriticality, PresumedCritical, ResidualAboveInherent];

    /// <summary>The <c>filters</c> term: <c>chip=default_criticality</c>.</summary>
    public static string Filter(string value) => Key + "=" + value;
}
