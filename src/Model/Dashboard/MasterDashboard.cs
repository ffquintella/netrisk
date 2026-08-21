using System;
using System.Collections.Generic;

namespace Model.Dashboard;

/// <summary>
/// Cross-entity aggregate served by <c>GET /Dashboard/Master</c> (Track 2 milestone 2.3.3).
/// One round trip returns every entity's rollup: the spec forbids the client fanning out a
/// request per entity, so all the arithmetic happens server-side.
/// </summary>
public class MasterDashboard
{
    /// <summary>When the rollup was computed. Non-null even on a cache hit, so the GUI can show staleness.</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>True when this payload came from the short server-side cache rather than a fresh query.</summary>
    public bool FromCache { get; set; }

    /// <summary>Per-entity cards, ordered worst-posture first.</summary>
    public List<EntityPostureSummary> Entities { get; set; } = new();

    /// <summary>
    /// Organisation-wide totals. These are the sum of <see cref="Entities"/>, including the
    /// unassigned bucket, so the figures reconcile with each entity's own dashboard.
    /// </summary>
    public EntityPostureSummary Totals { get; set; } = new();
}
