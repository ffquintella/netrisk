using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using Model.DecisionCycle;

namespace Tools.DecisionCycle;

/// <summary>
/// The pure rules of the archive (Stage 9.9, S50 §4.1–§4.3): whether an archive governs its risk, when it is due for its
/// quarterly review, and whether an event of a given type reopens it.
/// </summary>
public static class ArchiveRules
{
    /// <summary>The register's status string for a closed risk.</summary>
    public const string ClosedStatus = "Closed";

    /// <summary>
    /// Live (S50 D1): the record says archived, the risk is closed, and its closure is still the one the archive made.
    /// <paramref name="archiveClosureExists"/> is whether that closure row still exists for the risk: the legacy reopen
    /// route deletes it, and re-closing makes a different one.
    /// </summary>
    public static RiskArchiveState StateOf(RiskArchiveStatus status, int? archiveClosureId, string? riskStatus,
        bool archiveClosureExists)
    {
        if (status == RiskArchiveStatus.Reopened) return RiskArchiveState.Reopened;

        return archiveClosureId is not null && archiveClosureExists &&
               string.Equals(riskStatus, ClosedStatus, StringComparison.OrdinalIgnoreCase)
            ? RiskArchiveState.Live
            : RiskArchiveState.Superseded;
    }

    /// <summary>A quarter after <paramref name="fromUtc"/> — archiving, or the review that kept it.</summary>
    public static DateTime NextReviewDue(DateTime fromUtc) => fromUtc.AddMonths(DecisionCycleLimits.ReviewIntervalMonths);

    /// <summary>Due on the date itself, not the day after.</summary>
    public static bool IsReviewDue(DateTime nextReviewDueAt, DateTime nowUtc) => nowUtc >= nextReviewDueAt;

    /// <summary>
    /// Whether an event of <paramref name="type"/> reopens an archive watching <paramref name="watched"/> (S50 D3). One
    /// type is enough; the event must reach the risk — declared naming it, or a breach of a KRI linked to it.
    /// </summary>
    public static bool Reopens(IEnumerable<ReassessmentTriggerType> watched, ReassessmentTriggerType type) =>
        watched.Contains(type);
}
