using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Models;
using GUIClient.Navigation;
using GUIClient.Tools.Navigation;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>The detail's header (§5.9.2): kind, class, nodes, criticality with origin and author, and context.</summary>
public sealed record ObjectDetailHeader(string Title, string Snapshot, string KindAndClass, string Nodes, string Criticality,
    string CriticalityOrigin, string? InternetFacing, string? VendorReference, string? RedactionNote);

/// <summary>"Por categoria": R, state, population, weight and branch.</summary>
public sealed record ObjectDetailCategoryRow(string Category, string R, string State, string Population, string Weight, string Branch);

/// <summary>"Fatores": type, x, validity, u_f, F_eff, the reading's time and its source.</summary>
public sealed record ObjectDetailFactorRow(string Category, string Factor, string Type, string X, string V, string UF, string FEff,
    string Reading, string Source);

/// <summary>A navigation destination: enabled by <see cref="ModuleAccess"/>, otherwise with the reason (IX-4, IX-7).</summary>
public sealed record ObjectDetailDestination(RiskIndexNavigationModule Module, string Label, NavigationTarget? Target,
    bool IsEnabled, string? DisabledReason);

/// <summary>
/// The object detail's wording (S40 §5.9.2): what the server read for one object on the snapshot day — factor readings,
/// freshness, signals and the register's inputs — under the §11.3 redaction: without the owner module's permission the
/// scenario, decision state, signals and destinations give way to "Requer a permissão ‹módulo›". Values only; no actions.
/// </summary>
public static class ObjectDetailFormatter
{
    /// <summary>Signals listed per factor (<c>k_sinais_max</c>, S39 §9.1).</summary>
    public const int MaxSignals = 50;

    private const string Dash = "—";

    public static ObjectDetailHeader Header(RiskIndexObjectDetailDto detail, TextLookup text, CultureInfo culture)
    {
        var label = ObjectLabelDisplay.Of(detail.Object, text, culture);
        var kind = RiskText.Get(text, "RiskObjectKind_" + detail.Object.Object.Kind);
        var assetClass = detail.AssetClass is { Length: > 0 } cls
            ? Enum.TryParse<RiskIndexAssetClass>(cls, out var parsed) ? text(NodeKeyDisplay.AssetClassKey(parsed)) ?? cls : cls
            : Dash;
        var nodes = detail.Attribution.Count == 0
            ? RiskText.Get(text, "RiskObjectDetailNoNodes")
            : RiskText.Format(text, "RiskObjectDetailNodesFMT", culture, detail.Attribution.Count,
                string.Join(", ", detail.Attribution.Select(n => n.Label)));

        var c = detail.Criticality;
        var criticality = RiskText.Format(text, "RiskObjectDetailCriticalityFMT", culture, c.Effective,
            c.Own?.ToString(culture) ?? Dash, c.Inherited?.ToString(culture) ?? Dash, c.DataDelta.ToString(culture));
        var origin = RiskText.Format(text, "RiskObjectDetailOriginFMT", culture,
            text("RiskCriticalitySource_" + c.Source) ?? c.Source);
        if (c.DeclaredNotBia) origin += " (" + RiskText.Get(text, "RiskObjectDetailDeclaredNotBia") + ")";
        if (c.AuthorName is { Length: > 0 } author)
            origin += " · " + (c.ChangedAtUtc is { } at
                ? RiskText.Format(text, "RiskObjectDetailAuthorAtFMT", culture, author, at.ToLocalTime().ToString("d", culture))
                : RiskText.Format(text, "RiskObjectDetailAuthorFMT", culture, author));

        string? internet = detail.InternetFacing switch
        {
            true => RiskText.Get(text, "RiskObjectDetailInternetFacingYes"),
            false => RiskText.Get(text, "RiskObjectDetailInternetFacingNo"),
            null => null
        };
        var vendor = RiskIndexNavigationMap.IsVendorObject(detail.Object.Object.Kind)
            ? RiskText.Format(text, "RiskObjectDetailVendorIdFMT", culture, detail.Object.Object.Ref)
            : null;

        return new ObjectDetailHeader(RiskText.Format(text, "RiskObjectDetailTitleFMT", culture, label.Label),
            RiskText.Format(text, "RiskObjectDetailSnapshotFMT", culture, detail.SnapshotDate.ToString("d", culture)),
            RiskText.Format(text, "RiskObjectDetailKindFMT", culture, kind, assetClass), nodes, criticality, origin, internet,
            vendor, label.Hint);
    }

    public static IReadOnlyList<ObjectDetailCategoryRow> Categories(RiskIndexObjectDetailDto detail, TextLookup text,
        CultureInfo culture) =>
        detail.Categories.Select(k => new ObjectDetailCategoryRow(HeadlineFormatter.CategoryCode(k.Category),
            k.R.ToString("0.000", culture), RiskText.Get(text, "RiskObjectState_" + k.State),
            text("RiskPopulation_" + k.Population) ?? k.Population, k.Weight.ToString("0.00", culture),
            RiskText.Get(text, "RiskContributionBranch_" + k.Branch))).ToList();

    public static IReadOnlyList<ObjectDetailFactorRow> Factors(RiskIndexObjectDetailDto detail, TextLookup text,
        CultureInfo culture) =>
        detail.Categories.SelectMany(k => k.Factors.Select(f => new ObjectDetailFactorRow(HeadlineFormatter.CategoryCode(k.Category),
            f.FactorKey, f.Type, f.X.ToString("0.000", culture), f.V.ToString("0.00", culture), f.UF.ToString("0.0", culture),
            f.FEff.ToString("0.000", culture), f.ReadingUtc?.ToLocalTime().ToString("g", culture) ?? Dash,
            f.SourceKey is { Length: > 0 } source ? text("RiskSource_" + source) ?? source : Dash))).ToList();

    /// <summary>
    /// "Sinais": up to <see cref="MaxSignals"/> per factor — "EXP.V · s 0,900 · kev, m_net · achado #45122". A factor whose
    /// signals the caller may not read keeps their values and says which permission their text needs (§11.3).
    /// </summary>
    public static IReadOnlyList<string> Signals(RiskIndexObjectDetailDto detail, TextLookup text, CultureInfo culture)
    {
        var lines = new List<string>();
        foreach (var factor in detail.Categories.SelectMany(k => k.Factors))
        {
            var signals = factor.Signals.Take(MaxSignals).ToList();
            foreach (var s in signals)
            {
                var parts = new List<string> { factor.FactorKey, RiskText.Format(text, "RiskObjectDetailSignalFMT", culture, s.S.ToString("0.000", culture)) };
                if (s.Flags.Count > 0) parts.Add(string.Join(", ", s.Flags));
                if (s.Text is { Length: > 0 } source) parts.Add(source);
                lines.Add(string.Join(" · ", parts));
            }

            if (signals.Any(s => s.Text is null))
                lines.Add(factor.FactorKey + " · " + RiskText.Format(text, ObjectLabelDisplay.RequiresPermissionKey, culture,
                    RiskIndexRedactionPolicy.SignalPermission(factor.FactorKey)));
            if (factor.Signals.Count > MaxSignals)
                lines.Add(factor.FactorKey + " · " + RiskText.Format(text, "RiskObjectDetailMoreSignalsFMT", culture,
                    factor.Signals.Count - MaxSignals));
        }

        return lines;
    }

    /// <summary>
    /// The register's inputs (S39 §6.1, I9): the effective residual beside the stored one with the note that the stored
    /// value is not changed; "Tratamento não avaliado" without a residual. Null for an object that is not a scenario.
    /// </summary>
    public static IReadOnlyList<string>? Scenario(RiskIndexObjectDetailDto detail, TextLookup text, CultureInfo culture)
    {
        if (detail.Object.Object.Kind != RiskIndexObjectKind.Scenario) return null;
        if (detail.Scenario is not { } s)
            return [Redaction(detail, text, culture) ?? Dash];

        string N(double? v) => v?.ToString("0.00", culture) ?? Dash;
        var lines = new List<string>
        {
            RiskText.Format(text, "RiskObjectDetailScenarioFMT", culture, s.ImpactLevel, s.Rank?.ToString(culture) ?? Dash,
                s.CampaignSize?.ToString(culture) ?? Dash, s.CadenceDays, s.DelayDays, N(s.Credit), N(s.SigmaRef)),
            s.RRes is { } residual
                ? RiskText.Format(text, "RiskObjectDetailResidualFMT", culture, N(s.REff), N(residual), N(s.RInh))
                : RiskText.Get(text, "RiskObjectDetailNotTreated")
        };
        return lines;
    }

    /// <summary>The read-only decision state, or the permission it needs, or "—".</summary>
    public static string Decision(RiskIndexObjectDetailDto detail, TextLookup text, CultureInfo culture) =>
        detail.DecisionState is { } state
            ? ContributionFormatter.DecisionText(state, text, culture)
            : Redaction(detail, text, culture) ?? Dash;

    public static IReadOnlyList<string> Chips(RiskIndexObjectDetailDto detail, TextLookup text) =>
        detail.Chips.Select(c => text("RiskChip_" + c.Key + "MSG") ?? c.Key).ToList();

    /// <summary>
    /// The "Navegação" section: every destination the kind has, enabled when the server offered it and the user may open the
    /// module (the navigation bar's own rule); disabled, with "Requer a permissão ‹módulo›" as the reason.
    /// </summary>
    public static IReadOnlyList<ObjectDetailDestination> Destinations(RiskIndexObjectDetailDto detail,
        Func<AvaliableViews, bool> canOpen, TextLookup text, CultureInfo culture)
    {
        var modules = RiskIndexNavigationMap.Destinations(detail.Object.Object.Kind)
            .Concat(detail.Navigation.Select(n => n.Module)).Distinct().ToList();
        var result = new List<ObjectDetailDestination>();
        foreach (var module in modules)
        {
            var offered = detail.Navigation.FirstOrDefault(n => n.Module == module);
            var target = offered is null ? null : RiskIndexNavigationMap.ToTarget(offered);
            if (RiskIndexNavigationMap.ViewOf(module) is not { } view) continue;

            var enabled = target is not null && canOpen(view);
            var reason = enabled
                ? null
                : RiskText.Format(text, ObjectLabelDisplay.RequiresPermissionKey, culture, ModuleAccess.RequiredPermission(view) ?? Dash);
            result.Add(new ObjectDetailDestination(module, RiskText.Get(text, RiskIndexNavigationMap.LabelKey(module)),
                enabled ? target : null, enabled, reason));
        }

        return result;
    }

    /// <summary>The note that replaces register content for a caller without the owner's permission; null when readable.</summary>
    public static string? Redaction(RiskIndexObjectDetailDto detail, TextLookup text, CultureInfo culture) =>
        detail.Object.RequiresPermission is { Length: > 0 } permission
            ? RiskText.Format(text, ObjectLabelDisplay.RequiresPermissionKey, culture, permission)
            : null;

    /// <summary>The resource keys this formatter computes, so a test can require them in every language.</summary>
    public static IEnumerable<string> ComputedKeys() =>
        Enum.GetValues<RiskIndexPopulation>().Select(p => "RiskPopulation_" + p)
            .Concat(Enum.GetValues<RiskIndexNavigationModule>().Select(RiskIndexNavigationMap.LabelKey))
            .Concat(new[]
            {
                "RiskObjectDetailNoNodes", "RiskObjectDetailNodesFMT", "RiskObjectDetailCriticalityFMT", "RiskObjectDetailOriginFMT",
                "RiskObjectDetailDeclaredNotBia", "RiskObjectDetailAuthorFMT", "RiskObjectDetailAuthorAtFMT",
                "RiskObjectDetailInternetFacingYes", "RiskObjectDetailInternetFacingNo", "RiskObjectDetailVendorIdFMT",
                "RiskObjectDetailTitleFMT", "RiskObjectDetailSnapshotFMT", "RiskObjectDetailKindFMT", "RiskObjectDetailSignalFMT",
                "RiskObjectDetailMoreSignalsFMT", "RiskObjectDetailScenarioFMT", "RiskObjectDetailResidualFMT",
                "RiskObjectDetailNotTreated"
            });
}
