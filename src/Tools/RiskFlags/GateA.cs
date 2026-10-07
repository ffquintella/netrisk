using DAL.Enums;
using Model.RiskFlags;

namespace Tools.RiskFlags;

/// <summary>
/// The Gate A predicate of MIGR-TI/IA Phase 4 over a risk's flags (Stage 9.5, S46 §4.7).
///
/// Gate A — "human safety, illegality, regulatory obligation, risk without legitimate acceptance, active
/// exploitation; no economic analysis overrides it; immediate escalation" — holds when flag 1, flag 2,
/// flag 3 or the declared condition "no legitimate acceptance" is set. Flag 4 is <b>not</b> a condition
/// with any weight (S46 D5): the methodology's Phase 4 table does not list it, and neither does S39 PA-1.
///
/// Pure and static so the predicate the ICR (S39 PA-1) reads is one function with one test, not a rule
/// re-derived in every caller.
/// </summary>
public static class GateA
{
    /// <summary>The non-discretionary codes, in code order.</summary>
    public static readonly IReadOnlyList<RiskFlagCode> Conditions =
    [
        RiskFlagCode.HumanSafety,
        RiskFlagCode.LegalRegulatory,
        RiskFlagCode.KnownExploitation,
        RiskFlagCode.NoLegitimateAcceptance
    ];

    /// <summary>True when the code is a Gate A condition.</summary>
    public static bool IsCondition(RiskFlagCode code) => Conditions.Contains(code);

    /// <summary>The Gate A conditions among the codes that are set, in code order, without repeats.</summary>
    public static List<RiskFlagCode> ConditionsAmong(IEnumerable<RiskFlagCode> setCodes) =>
        setCodes.Where(IsCondition).Distinct().OrderBy(c => (int)c).ToList();

    /// <summary>The predicate: any condition set.</summary>
    public static bool Holds(IEnumerable<RiskFlagCode> setCodes) => setCodes.Any(IsCondition);

    /// <summary>"1,3" — the form stored in <c>risk_decisions.gate_a_conditions</c>.</summary>
    public static string Format(IEnumerable<RiskFlagCode> conditions) =>
        string.Join(",", conditions.Select(c => ((int)c).ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The inverse of <see cref="Format"/>; unknown or malformed parts are dropped.</summary>
    public static List<RiskFlagCode> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return [];

        return stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? (RiskFlagCode?)n : null)
            .Where(c => c is not null && RiskFlagCatalogue.IsDefined(c.Value))
            .Select(c => c!.Value)
            .Distinct()
            .OrderBy(c => (int)c)
            .ToList();
    }

    /// <summary>
    /// The refusal text: which conditions hold and on what basis. Written to be shown verbatim to the person
    /// whose acceptance, closure or deletion was refused — in the portal as much as in the desktop.
    /// </summary>
    public static string Explain(GateAAction action, IReadOnlyList<(RiskFlagCode Code, string Basis)> holding)
    {
        var verb = action switch
        {
            GateAAction.Accept => "accepted",
            GateAAction.RenewAcceptance => "have its acceptance renewed",
            GateAAction.Close => "closed",
            GateAAction.Delete => "deleted",
            _ => "be decided anything but 'act immediately'"
        };

        var conditions = string.Join("; ", holding.Select(h =>
        {
            var descriptor = RiskFlagCatalogue.Find(h.Code);
            var label = descriptor?.Number is { } number ? $"flag {number} ({descriptor.Name})" : descriptor?.Name ?? h.Code.ToString();
            return string.IsNullOrWhiteSpace(h.Basis) ? label : $"{label}: {h.Basis}";
        }));

        return $"This risk cannot be {verb}: it carries a non-discretionary (Gate A) condition — {conditions}. " +
               "Gate A comes before the risk appetite and no economic analysis overrides it; the risk has to be " +
               "acted on immediately. If the condition no longer holds, correct the fact behind it (withdraw the " +
               "declaration with a written reason, or resolve the finding) and try again.";
    }
}
