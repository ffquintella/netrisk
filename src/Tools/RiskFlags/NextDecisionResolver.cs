using Model.RiskFlags;

namespace Tools.RiskFlags;

/// <summary>What the next decision of a Top Risks row is computed from (S46 §4.9). All dates UTC.</summary>
public sealed record NextDecisionInput
{
    /// <summary>Gate A holds on the risk.</summary>
    public bool GateA { get; init; }

    /// <summary>When the current Gate A condition began (the automatic decision), if known.</summary>
    public DateTime? GateASince { get; init; }

    /// <summary>The expiry of the live acceptance, if any.</summary>
    public DateTime? AcceptanceExpiresAt { get; init; }

    /// <summary>The next review the latest management review scheduled, if any.</summary>
    public DateTime? NextManagementReviewAt { get; init; }

    /// <summary>The earliest due date among the open treatment tasks — the caller excludes completed and cancelled ones.</summary>
    public DateTime? EarliestOpenTaskDueAt { get; init; }

    /// <summary>A Phase 4 decision has been recorded at least once.</summary>
    public bool HasDecision { get; init; }
}

/// <summary>
/// The "next decision" column of the Top Risks list (Stage 9.5, S46 §4.9): Gate A first and overdue since
/// its onset; otherwise the nearest dated event among acceptance expiry, next management review and the
/// earliest open task; otherwise <see cref="NextDecisionKind.DecisionPending"/> when nothing was ever
/// decided, <see cref="NextDecisionKind.None"/> when something was.
/// </summary>
public static class NextDecisionResolver
{
    public static NextDecisionDto Resolve(NextDecisionInput input, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.GateA)
            return new NextDecisionDto
            {
                Kind = NextDecisionKind.GateAEscalation,
                DueAt = input.GateASince ?? nowUtc,
                // Gate A means now: once it began, every moment after is late.
                Overdue = true
            };

        // Ties go to the order listed: an acceptance lapsing the same day a review is due is the more
        // consequential event, because lapsing reopens the risk.
        var candidates = new List<(NextDecisionKind Kind, DateTime At)>();
        if (input.AcceptanceExpiresAt is { } expiry) candidates.Add((NextDecisionKind.AcceptanceExpiry, expiry));
        if (input.NextManagementReviewAt is { } review) candidates.Add((NextDecisionKind.ManagementReview, review));
        if (input.EarliestOpenTaskDueAt is { } task) candidates.Add((NextDecisionKind.MitigationTaskDue, task));

        if (candidates.Count > 0)
        {
            var next = candidates.OrderBy(c => c.At).First();
            return new NextDecisionDto { Kind = next.Kind, DueAt = next.At, Overdue = next.At < nowUtc };
        }

        return new NextDecisionDto
        {
            Kind = input.HasDecision ? NextDecisionKind.None : NextDecisionKind.DecisionPending
        };
    }
}
