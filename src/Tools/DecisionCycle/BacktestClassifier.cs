using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using Model.DecisionCycle;

namespace Tools.DecisionCycle;

/// <summary>A window during which a risk was archived: from archiving to reopening (open-ended while live).</summary>
public readonly record struct ArchiveWindow(DateTime From, DateTime? To);

/// <summary>A risk acceptance's validity: from its start to its expiry, cut short by a revocation.</summary>
public readonly record struct AcceptanceWindow(DateTime From, DateTime ExpiresAt, DateTime? RevokedAt);

/// <summary>A Phase 4 decision and when it was recorded.</summary>
public readonly record struct DatedDecision(DateTime At, RiskDecisionKind Kind);

/// <summary>
/// What the register had decided about a risk over time — the facts that say whether it had been cut when an incident
/// occurred (S50 §4.4). <see cref="ClosedSince"/> is the current closure's date, when the risk is closed: a closure
/// removed by a reopening leaves no trace in <c>closures</c>, so a closure that ended before today is not seen.
/// </summary>
public sealed record RiskCutHistory(
    IReadOnlyList<ArchiveWindow> Archives,
    DateTime? ClosedSince,
    IReadOnlyList<AcceptanceWindow> Acceptances,
    IReadOnlyList<DatedDecision> Decisions)
{
    public static readonly RiskCutHistory None = new([], null, [], []);
}

/// <summary>One matched risk as the classification sees it.</summary>
public sealed record BacktestLink(int RiskId, DateTime RegisteredAt, IReadOnlyList<string> DismissalReasons)
{
    public bool Dismissed => DismissalReasons.Count > 0;
}

/// <summary>
/// Incident backtesting (Stage 9.9, S50 §4.4): an incident or near miss against the risks an assessor matched to it. Pure
/// and tested alone, because the one rule the methodology names is a date comparison that is easy to get backwards: a
/// scenario registered after the incident never counts as foreseen (S50 D6).
/// </summary>
public static class BacktestClassifier
{
    public const string Archived = "archived";
    public const string Closed = "closed";
    public const string Accepted = "accepted";
    public const string DecidedMonitorAccept = "decided monitor/accept";
    public const string DecidedArchive = "decided archive";

    /// <summary>
    /// When the incident occurred, as early as the record allows: the earliest of its start, its report and its creation
    /// in the register. The earliest is the conservative choice — the earlier the occurrence, the fewer risks count as
    /// registered before it.
    /// </summary>
    public static DateTime OccurrenceOf(DateTime? startDate, DateTime reportDate, DateTime creationDate)
    {
        var earliest = reportDate < creationDate ? reportDate : creationDate;
        return startDate is { } start && start < earliest ? start : earliest;
    }

    /// <summary>Strictly before: a risk registered at the same instant as the incident did not foresee it.</summary>
    public static bool Foresees(DateTime registeredAt, DateTime occurredAt) => registeredAt < occurredAt;

    /// <summary>
    /// Why the register had cut the risk at <paramref name="at"/>, or nothing: archived then; closed by the current
    /// closure dated at or before then; covered by an acceptance valid then; or its latest Phase 4 decision at or
    /// before then was "monitor/accept" or "archive".
    /// </summary>
    public static List<string> DismissalReasons(RiskCutHistory history, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(history);
        var reasons = new List<string>();

        if (history.Archives.Any(w => w.From <= at && (w.To is null || w.To > at))) reasons.Add(Archived);

        if (history.ClosedSince is { } closed && closed <= at) reasons.Add(Closed);

        if (history.Acceptances.Any(a => a.From <= at && at < a.ExpiresAt && (a.RevokedAt is null || a.RevokedAt > at)))
            reasons.Add(Accepted);

        var latest = history.Decisions.Where(d => d.At <= at).OrderByDescending(d => d.At).FirstOrDefault();
        if (history.Decisions.Any(d => d.At <= at))
        {
            if (latest.Kind == RiskDecisionKind.MonitorAccept) reasons.Add(DecidedMonitorAccept);
            if (latest.Kind == RiskDecisionKind.Archive) reasons.Add(DecidedArchive);
        }

        return reasons;
    }

    /// <summary>
    /// The outcome (S50 §4.4): not assessed; not foreseen (no match); registered after the occurrence (only matches
    /// written at or after it); foreseen and treated (a match registered before, not cut then); foreseen and dismissed
    /// (every match registered before had been cut) — the false negative of the cut.
    /// </summary>
    public static BacktestOutcome Classify(bool assessed, DateTime occurredAt, IReadOnlyCollection<BacktestLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        if (!assessed) return BacktestOutcome.NotAssessed;
        if (links.Count == 0) return BacktestOutcome.NotForeseen;

        var foreseeing = links.Where(l => Foresees(l.RegisteredAt, occurredAt)).ToList();
        if (foreseeing.Count == 0) return BacktestOutcome.RegisteredAfterOccurrence;

        return foreseeing.Any(l => !l.Dismissed) ? BacktestOutcome.ForeseenTreated : BacktestOutcome.ForeseenDismissed;
    }

    /// <summary>The counts and rates of a period (S50 §4.4), filled into <paramref name="report"/>.</summary>
    public static void Summarize(BacktestReportDto report, IEnumerable<(IncidentKind Kind, BacktestOutcome Outcome)> items)
    {
        ArgumentNullException.ThrowIfNull(report);
        var all = items.ToList();

        report.Incidents = all.Count(i => i.Kind == IncidentKind.Incident);
        report.NearMisses = all.Count(i => i.Kind == IncidentKind.NearMiss);
        report.NotAssessed = all.Count(i => i.Outcome == BacktestOutcome.NotAssessed);
        report.Assessed = all.Count - report.NotAssessed;
        report.NotForeseen = all.Count(i => i.Outcome == BacktestOutcome.NotForeseen);
        report.RegisteredAfterOccurrence = all.Count(i => i.Outcome == BacktestOutcome.RegisteredAfterOccurrence);
        report.ForeseenTreated = all.Count(i => i.Outcome == BacktestOutcome.ForeseenTreated);
        report.ForeseenDismissed = all.Count(i => i.Outcome == BacktestOutcome.ForeseenDismissed);

        var foreseen = report.ForeseenTreated + report.ForeseenDismissed;
        report.UnforeseenRate = report.Assessed == 0
            ? null
            : (double)(report.NotForeseen + report.RegisteredAfterOccurrence) / report.Assessed;
        report.FalseNegativeRate = foreseen == 0 ? null : (double)report.ForeseenDismissed / foreseen;
        report.AssessedShare = all.Count == 0 ? null : (double)report.Assessed / all.Count;
    }
}
