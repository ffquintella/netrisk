using DAL.Entities;
using Model.Exceptions;
using Model.TreatmentEconomics;

namespace Tools.TreatmentEconomics;

/// <summary>
/// A declared monetary cost of a treatment and the two figures the gates read from it (Stage 9.6, S47 §4.5):
/// <list type="bullet">
/// <item><see cref="AnnualizedTotal"/> — Gate C's cost: annual + side effects + one-time ÷ horizon. Annual,
/// because the E[L] it is compared with is an annual figure; with the side effects, because the methodology
/// says "the total cost and the side effects".</item>
/// <item><see cref="FirstYear"/> — Gate D's budget draw: one-time + annual. A 300 000 project amortized over
/// three years still needs 300 000 now; side effects are a cost but not an outlay from the treatment budget
/// (S47 D4).</item>
/// </list>
/// A cost is a block: all three amounts or none. "None" is <em>not declared</em> and is never read as zero.
/// </summary>
public sealed record TreatmentCost(decimal OneTime, decimal Annual, decimal SideEffectsAnnual, int? HorizonYears)
{
    /// <summary>Annual + side effects + one-time ÷ horizon (the one-time term is zero when the one-time cost is).</summary>
    public decimal AnnualizedTotal =>
        Annual + SideEffectsAnnual + (OneTime > 0 && HorizonYears is > 0 ? OneTime / HorizonYears.Value : 0m);

    /// <summary>One-time + annual — what the first year draws from the budget.</summary>
    public decimal FirstYear => OneTime + Annual;

    /// <summary>The stored cost, or null when none is declared (or the block is incomplete, which the schema refuses).</summary>
    public static TreatmentCost? FromStored(MitigationEconomics? economics) =>
        economics is { CostOneTime: { } oneTime, CostAnnual: { } annual, CostSideEffectsAnnual: { } side }
            ? new TreatmentCost(oneTime, annual, side, economics.CostHorizonYears)
            : null;

    /// <summary>
    /// Validates a requested cost and returns it. Every amount is required, ≥ 0 and ≤ 10¹²; the horizon is
    /// required (1–30) when the one-time cost is above zero and ignored otherwise.
    /// </summary>
    /// <exception cref="InvalidParameterException">Naming the offending field, <c>Cost.&lt;Field&gt;</c>.</exception>
    public static TreatmentCost FromRequest(TreatmentCostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var oneTime = Amount(request.OneTime, "Cost.OneTime", "the one-time (implementation) cost");
        var annual = Amount(request.Annual, "Cost.Annual", "the recurring annual cost");
        var side = Amount(request.SideEffectsAnnual, "Cost.SideEffectsAnnual", "the annual side-effects cost");

        int? horizon = null;
        if (oneTime > 0)
        {
            if (request.HorizonYears is not { } years)
                throw new InvalidParameterException("Cost.HorizonYears",
                    "A one-time cost needs the number of years it is amortized over, so it can be compared with an " +
                    "annual expected loss.");

            if (years is < TreatmentEconomicsLimits.MinHorizonYears or > TreatmentEconomicsLimits.MaxHorizonYears)
                throw new InvalidParameterException("Cost.HorizonYears",
                    $"The amortization horizon is {TreatmentEconomicsLimits.MinHorizonYears} to " +
                    $"{TreatmentEconomicsLimits.MaxHorizonYears} years.");

            horizon = years;
        }

        return new TreatmentCost(oneTime, annual, side, horizon);
    }

    public TreatmentCostDto ToDto() => new()
    {
        OneTime = OneTime,
        Annual = Annual,
        SideEffectsAnnual = SideEffectsAnnual,
        HorizonYears = HorizonYears,
        AnnualizedTotal = decimal.Round(AnnualizedTotal, 2, MidpointRounding.AwayFromZero),
        FirstYear = FirstYear
    };

    private static decimal Amount(decimal? value, string parameter, string what)
    {
        if (value is not { } amount)
            throw new InvalidParameterException(parameter,
                $"A monetary cost is declared as a block: {what} is required (zero is a valid amount). To say no " +
                "cost is known, send no cost at all — Gate C then reports the treatment as not assessable.");

        if (amount < 0)
            throw new InvalidParameterException(parameter, $"{Capitalize(what)} cannot be negative.");

        if (amount > TreatmentEconomicsLimits.MaxAmount)
            throw new InvalidParameterException(parameter,
                $"{Capitalize(what)} is above the largest accepted amount ({TreatmentEconomicsLimits.MaxAmount:N0}).");

        return amount;
    }

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
