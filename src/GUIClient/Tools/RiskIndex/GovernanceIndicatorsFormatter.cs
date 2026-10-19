using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GUIClient.Models;
using GUIClient.Tools.Navigation;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// One governance or review indicator (S40 §5.2.5): its sentence, and the module its "›" opens — enabled only when the user
/// may open that module (<c>ModuleAccess</c>, §10.3), otherwise disabled with the reason. Null module: no "›".
/// </summary>
public sealed record GovernanceIndicator(string Key, string Text, AvaliableViews? Module, bool CanOpen, string? Hint)
{
    public bool HasModule => Module is not null;
}

/// <summary>
/// The governance and review indicators beside the index (S40 §5.2.5; T256): volume, never risk, never in the number nor in
/// any order (S39 §1.2, §6.3). Pure. The server counts them live (T249), so they never follow a past snapshot date.
/// </summary>
public static class GovernanceIndicatorsFormatter
{
    private const string Dash = "—";

    public static IReadOnlyList<GovernanceIndicator> Items(RiskIndexGovernanceIndicatorsDto dto, Func<AvaliableViews, bool> canOpen,
        TextLookup text, CultureInfo culture)
    {
        var sla = dto.SlaCompliance is { } compliance ? Percent(compliance, culture) : Dash;
        return
        [
            Linked("acceptances", RiskText.Format(text, "RiskGovernance_AcceptancesExpiringFMT", culture, dto.ExpiryWindowDays,
                dto.AcceptancesExpiringSoon.ToString("N0", culture)), AvaliableViews.Risk, canOpen, text, culture),
            Linked("segregation", RiskText.Format(text, "RiskGovernance_SegregationOverridesFMT", culture,
                dto.SegregationOverrides90d.ToString("N0", culture)), AvaliableViews.Risk, canOpen, text, culture),
            // Review campaigns have no module in the desktop shell: the sentence, without a "›".
            new("campaigns", RiskText.Format(text, "RiskGovernance_CampaignsFMT", culture,
                dto.CampaignCompletion is { } completion ? Percent(completion, culture) : Dash,
                dto.MeanDaysToDecide is { } days ? RiskText.Format(text, "RiskGovernance_DaysFMT", culture, days.ToString("0.#", culture)) : Dash,
                dto.OverdueCampaigns.ToString("N0", culture)), null, false, null),
            Linked("sla", RiskText.Format(text, "RiskGovernance_SlaComplianceFMT", culture, sla), AvaliableViews.Vulnerabilities,
                canOpen, text, culture, dto.SlaCompliance is null ? RiskText.Get(text, "RiskGovernance_SlaNoneHint") : null),
            new("flags", RiskText.Format(text, "RiskGovernance_FlagsFMT", culture, State(dto.FlagsStateKey, text)), null, false, null),
            new("gate_b", RiskText.Format(text, "RiskGovernance_GateBFMT", culture, State(dto.GateBStateKey, text)), null, false, null)
        ];
    }

    /// <summary>"não disponível (requer M43)" for a state key the interface knows; the key itself otherwise.</summary>
    public static string State(string key, TextLookup text) => text("RiskGovernance_State_" + key) ?? key;

    private static GovernanceIndicator Linked(string key, string sentence, AvaliableViews module, Func<AvaliableViews, bool> canOpen,
        TextLookup text, CultureInfo culture, string? note = null)
    {
        var allowed = canOpen(module);
        var name = RiskText.Get(text, ModuleAccess.NameKey(module));
        var hint = RiskText.Format(text, allowed ? "RiskGovernance_OpenModuleFMT" : "RiskGovernance_ModuleNotAllowedFMT", culture, name);
        return new GovernanceIndicator(key, sentence, module, allowed, note is null ? hint : note + " · " + hint);
    }

    private static string Percent(double fraction, CultureInfo culture) => HeadlineFormatter.Percent(fraction, culture) + "%";

    /// <summary>The state keys the server sends today (T249).</summary>
    public static readonly IReadOnlyList<string> KnownStateKeys = ["requires_m43", "requires_m45_m46"];

    public static IEnumerable<string> ComputedKeys() =>
        KnownStateKeys.Select(k => "RiskGovernance_State_" + k).Concat(
        [
            "RiskGovernance_AcceptancesExpiringFMT", "RiskGovernance_SegregationOverridesFMT", "RiskGovernance_CampaignsFMT",
            "RiskGovernance_DaysFMT", "RiskGovernance_SlaComplianceFMT", "RiskGovernance_SlaNoneHint", "RiskGovernance_FlagsFMT",
            "RiskGovernance_GateBFMT", "RiskGovernance_OpenModuleFMT", "RiskGovernance_ModuleNotAllowedFMT"
        ]);
}
