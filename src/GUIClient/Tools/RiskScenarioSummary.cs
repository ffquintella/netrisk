using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.Risks.Scenario;

namespace GUIClient.Tools;

/// <summary>
/// The pure half of the Stage 9.2 screens (S42 §7): how a scenario field and a confidence level read
/// in the risk detail, which localization key names each enum value, when the risk editor asks the
/// server for duplicates, and how the duplicate warning lists them.
///
/// No Avalonia types and nothing from <c>Tools</c>, so <c>GUIClient.Tests</c> compiles this file
/// directly — that project deliberately does not reference <c>GUIClient</c>.
/// </summary>
public static class RiskScenarioSummary
{
    /// <summary>A scenario field as the detail shows it: the text, or <paramref name="notInformed"/> — never blank.</summary>
    public static string Field(string? text, string notInformed) =>
        string.IsNullOrWhiteSpace(text) ? notInformed : text.Trim();

    /// <summary>
    /// The localization key of a confidence level. NULL is "not declared" — a legacy risk's honest
    /// state — and has a key of its own rather than borrowing one of the three levels.
    /// </summary>
    public static string ConfidenceKey(EvidenceConfidence? confidence) => confidence switch
    {
        EvidenceConfidence.Confirmed => "EvidenceConfidenceConfirmed",
        EvidenceConfidence.Indicative => "EvidenceConfidenceIndicative",
        EvidenceConfidence.Hypothesis => "EvidenceConfidenceHypothesis",
        _ => "EvidenceConfidenceNotDeclared"
    };

    /// <summary>The editor's choices, in order: not declared, then the three levels from strongest to weakest.</summary>
    public static IReadOnlyList<EvidenceConfidence?> ConfidenceChoices { get; } =
        [null, EvidenceConfidence.Confirmed, EvidenceConfidence.Indicative, EvidenceConfidence.Hypothesis];

    /// <summary>The localization key of a pending risk's origin.</summary>
    public static string OriginKey(PendingRiskOrigin origin) => origin == PendingRiskOrigin.Standalone
        ? "PendingOriginStandalone"
        : "PendingOriginAssessment";

    /// <summary>The localization key of an incident's kind.</summary>
    public static string KindKey(IncidentKind kind) => kind == IncidentKind.NearMiss
        ? "IncidentKindNearMiss"
        : "IncidentKindIncident";

    /// <summary>The editor's kind choices, incident first because it is the default.</summary>
    public static IReadOnlyList<IncidentKind> KindChoices { get; } = [IncidentKind.Incident, IncidentKind.NearMiss];

    /// <summary>
    /// Whether saving should first ask the server for duplicates (T154). Never without both halves —
    /// half a pair matches nothing and the server refuses it. Always on create. On edit only when the
    /// pair changed, so re-saving a risk does not warn again about a duplicate the user already
    /// accepted; the comparison is trimmed and case-insensitive, the server's own normalization being
    /// the authority on what actually matches.
    /// </summary>
    public static bool ShouldCheckDuplicates(bool creating, string? originalEvent, string? originalConsequences,
        string? centralEvent, string? consequences)
    {
        if (string.IsNullOrWhiteSpace(centralEvent) || string.IsNullOrWhiteSpace(consequences)) return false;
        if (creating) return true;

        return !Same(originalEvent, centralEvent) || !Same(originalConsequences, consequences);

        static bool Same(string? a, string? b) =>
            string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The warning's list: one line per matching risk, "#id subject (status)", in the server's order,
    /// capped at <paramref name="max"/> lines with a final "+n" so a long list does not push the
    /// question off the dialog.
    /// </summary>
    public static string DuplicateLines(IEnumerable<RiskScenarioDuplicate>? duplicates, int max = 5)
    {
        var all = (duplicates ?? []).ToList();
        var lines = all.Take(max)
            .Select(d => string.Create(CultureInfo.InvariantCulture, $"#{d.RiskId} {d.Subject} ({d.Status})"))
            .ToList();

        if (all.Count > max) lines.Add(string.Create(CultureInfo.InvariantCulture, $"+{all.Count - max}"));

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>One entry of a localized choice list (a ComboBox item): the value saved, the text shown.</summary>
public sealed record ChoiceOption<T>(T Value, string Name);
