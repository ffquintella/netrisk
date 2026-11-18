using System;

namespace DAL.Entities;

/// <summary>
/// M58, T291 (S39 §3.2 CMDB-INET, §5.3): whether the host is reachable from the internet, who said so and when — the
/// evidence the risk index's <c>m_net</c> reads for a host's findings, in place of the public-IP heuristic.
/// </summary>
public partial class Host
{
    /// <summary>True or false when a writer stated it; null when nobody has (the index then falls back to the heuristic).</summary>
    public bool? InternetFacing { get; set; }

    /// <summary>
    /// Who wrote <see cref="InternetFacing"/>: <see cref="AssetContextSources.Manual"/> or <see cref="AssetContextSources.Cmdb"/>;
    /// null when the value is null. The hosts API stamps it and never takes it from a body; an import never overwrites a
    /// manual value.
    /// </summary>
    public string? InternetFacingSource { get; set; }

    /// <summary>
    /// When the writer last stated the value, in UTC: a CMDB value older than the CMDB-INET window W (30 days by default)
    /// no longer counts; a manual one does not age. Server-owned like the source.
    /// </summary>
    public DateTime? InternetFacingSeenAt { get; set; }
}
