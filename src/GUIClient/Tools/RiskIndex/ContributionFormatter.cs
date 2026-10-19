using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// One line of the contributions (§5.8). <see cref="PointsValue"/>, <see cref="ObjectSort"/> and <see cref="CategoryOrder"/>
/// are what the three sortable columns sort by; the sensitivity has no sort value on purpose (S39 §1.3).
/// </summary>
public sealed record ContributionRow(RiskIndexContributionRowDto Source, string Object, string? ObjectHint, string Category,
    string Points, string Share, string Branch, string Sensitivity, string NextHolder, string? NextHolderHint, string Decision)
{
    public double PointsValue => Source.Points;
    public string ObjectSort => Object;
    public int CategoryOrder => (int)Source.Category;
    public bool IsRedacted => ObjectHint is not null;
}

/// <summary>
/// "Contribuições e sensibilidade do indicador" (S40 §5.8, S39 §4.8): how much of the number each object carries, the
/// branch it is in, the index's change if its reading cleared (<c>ChangeIfCleared</c>, a whole number) and, read-only, its
/// decision state. Not an order of treatment (R-1): nothing here ranks work, so the rows only navigate.
/// </summary>
public static class ContributionFormatter
{
    private const string Dash = "—";
    public const string R1NoticeKey = "RiskR1NoticeMSG";

    /// <summary>The server's order: points, largest first (the top contributions of the snapshot).</summary>
    public static IReadOnlyList<ContributionRow> Rows(RiskIndexContributionsDto contributions, TextLookup text, CultureInfo culture) =>
        contributions.Rows.OrderByDescending(r => r.Points).Select(r => Row(r, text, culture)).ToList();

    public static ContributionRow Row(RiskIndexContributionRowDto row, TextLookup text, CultureInfo culture)
    {
        var label = ObjectLabelDisplay.Of(row.Object, text, culture);
        var next = row.NextHolder is { } n ? ObjectLabelDisplay.Of(n, text, culture) : null;
        return new ContributionRow(row, label.Label, label.Hint, HeadlineFormatter.CategoryCode(row.Category),
            Whole(row.Points).ToString(culture), HeadlineFormatter.Percent(row.ShareOfCategory, culture) + "%",
            RiskText.Get(text, "RiskContributionBranch_" + row.Branch),
            row.Sensitivity is { } s ? HeadlineFormatter.SignedPoints(Whole(s), culture) : Dash,
            next?.Label ?? Dash, next?.Hint, Decision(row, text, culture));
    }

    /// <summary>
    /// The decision state (read-only): its parts when the server sends it; for a scenario whose label the caller may not
    /// read, "Requer a permissão riskmanagement" (§11.3); otherwise "—".
    /// </summary>
    public static string Decision(RiskIndexContributionRowDto row, TextLookup text, CultureInfo culture)
    {
        if (row.DecisionState is { } state) return DecisionText(state, text, culture);
        if (row.Object.Object.Kind is RiskIndexObjectKind.Scenario or RiskIndexObjectKind.Supplier &&
            row.Object.RequiresPermission is { Length: > 0 } permission)
            return RiskText.Format(text, ObjectLabelDisplay.RequiresPermissionKey, culture, permission);
        return Dash;
    }

    /// <summary>"aceite expirado 20/09; última decisão: mantido 25/08; 2 tarefas (1 vencida)" — whatever the server sent.</summary>
    public static string DecisionText(RiskIndexDecisionStateDto state, TextLookup text, CultureInfo culture)
    {
        var parts = new List<string>();
        if (state.AcceptanceStateKey is { Length: > 0 } acceptance)
            parts.Add(RiskText.Format(text, "RiskDecisionAcceptanceFMT", culture, text("RiskDecision_" + acceptance) ?? acceptance,
                state.AcceptanceValidUntilUtc?.ToLocalTime().ToString("d", culture) ?? Dash));
        if (state.LastCampaignDecisionKey is { Length: > 0 } campaign)
            parts.Add(RiskText.Format(text, "RiskDecisionCampaignFMT", culture, text("RiskDecision_" + campaign) ?? campaign,
                state.LastCampaignDecisionAtUtc?.ToLocalTime().ToString("d", culture) ?? Dash));
        if (state.OpenTasks > 0)
            parts.Add(RiskText.Format(text, "RiskDecisionTasksFMT", culture, state.OpenTasks, state.OverdueTasks));
        return parts.Count == 0 ? Dash : string.Join("; ", parts);
    }

    /// <summary>"Peso efetivo do registro: 51% da manchete…" (S39 §11.2, §13.2).</summary>
    public static string RegisterWeight(RiskIndexContributionsDto contributions, TextLookup text, CultureInfo culture) =>
        RiskText.Format(text, "RiskContributionsRegisterWeightFMT", culture,
            HeadlineFormatter.Percent(contributions.EffectiveRegisterWeight, culture));

    private static int Whole(double points) => (int)Math.Round(points, MidpointRounding.AwayFromZero);

    /// <summary>The resource keys this formatter computes, so a test can require them in every language.</summary>
    public static IEnumerable<string> ComputedKeys() =>
        Enum.GetValues<RiskIndexContributionBranch>().Select(b => "RiskContributionBranch_" + b)
            .Concat(new[]
            {
                R1NoticeKey, "RiskContributionsRegisterWeightFMT", "RiskDecisionAcceptanceFMT", "RiskDecisionCampaignFMT",
                "RiskDecisionTasksFMT"
            });
}
