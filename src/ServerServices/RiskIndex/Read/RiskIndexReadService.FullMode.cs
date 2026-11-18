using System.Globalization;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.RiskIndex;
using Model.TailRisk;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

/// <summary>
/// The full-mode reads of the dashboard (S39 §15.1 "ao lado"; S40 §5.10, §5.11; M59, T286, T287): Top Risks and the monetary
/// panel. Both consume Track 9's own services through the caller's scope (M43's ranking, M45's portfolio aggregation); the
/// index adds only the node's set, never a computation of its own.
/// </summary>
public sealed partial class RiskIndexReadService
{
    public const int MaxTopRisks = 50;

    public async Task<RiskIndexTopRisksDto> GetTopRisksAsync(string? node, DateOnly? date, int limit, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        if (limit is < 1 or > MaxTopRisks)
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidLimit, $"The Top Risks list holds 1 to {MaxTopRisks} risks.");

        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(ParseKey(node), day, ct);
        var extra = limit.ToString(CultureInfo.InvariantCulture);
        return await cache.GetOrCreateAsync(CacheKey("toprisks", resolved, day, caller, extra), RiskIndexReadCache.SnapshotTimeToLive,
            async () =>
            {
                // S39 §1.2 item 5, §15.1: no approximation in the interim mode — the space says why it is empty. Without
                // riskmanagement (S40 §11.3) a row keeps its rank and Gate A, as the floor's reason does, and loses the rest.
                if (day.Profile.Mode != RiskIndexMode.Full)
                    return new RiskIndexTopRisksDto(resolved.Ref, day.Date, false, RiskIndexErrors.RequiresFullMode, 0, []);
                if (riskFlags is null)
                    return new RiskIndexTopRisksDto(resolved.Ref, day.Date, false, RiskIndexErrors.Track9ServiceUnavailable, 0, []);

                var composition = await ComposeAsync(resolved, day, caller, ct);
                var scenarios = ScenarioIds(composition.Rows);
                if (scenarios.Count == 0) return new RiskIndexTopRisksDto(resolved.Ref, day.Date, true, null, 0, []);

                var top = await riskFlags.GetTopRisksAsync(limit, scenarios);

                await using var db = dalService.GetContext();
                var keys = top.Items.Select(i => new IcrObjectKey(RiskIndexObjectKind.Scenario,
                    i.RiskId.ToString(CultureInfo.InvariantCulture))).ToList();
                var labels = await RiskIndexLabels.ResolveAsync(db, keys, caller, ct);
                var register = RiskIndexRedactionPolicy.Holds(RiskIndexModulePermissions.RiskManagement, caller.IsAdmin,
                    caller.Permissions);

                var items = top.Items.Select((i, n) => new RiskIndexTopRiskDto(i.Rank, labels[keys[n]], i.GateA,
                    i.GateAConditions.Select(c => (int)c).ToList(), register ? (int?)i.Decision : null,
                    register ? i.Inherent : null, register ? i.Residual : null, register ? i.ExpectedAnnualLoss : null,
                    register ? i.BusinessRank : null, register ? (int?)i.Confidence : null,
                    register ? i.Trend.Direction.ToString() : null, register ? i.NextDecision.Kind.ToString() : null,
                    register ? new RiskIndexNavigationTargetDto(RiskIndexNavigationModule.Risk, i.RiskId, "RiskNav_Risk") : null))
                    .ToList();
                return new RiskIndexTopRisksDto(resolved.Ref, day.Date, true, null, top.OpenRisks, items);
            });
    }

    public async Task<RiskIndexMonetaryDto> GetMonetaryAsync(string? node, DateOnly? date, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(ParseKey(node), day, ct);
        return await cache.GetOrCreateAsync(CacheKey("monetary", resolved, day, caller), RiskIndexReadCache.SnapshotTimeToLive,
            async () =>
            {
                var (rows, quantities) = await budget.RunAsync(caller.UserId, async token =>
                {
                    await using var db = dalService.GetContext();
                    var composed = await ComposeWithinAsync(db, resolved, day, token);
                    var ids = ScenarioIds(composed.Rows);
                    // The caller's scoped context: a scenario they cannot see never enters a sum (S39 §10.4).
                    var scoring = await db.RiskScorings.AsNoTracking().Where(s => ids.Contains(s.Id))
                        .Select(s => new ScenarioLoss(s.Id, s.QuantResidualAleMean, s.QuantAleMean)).ToListAsync(token);
                    return (composed.Rows, scoring);
                }, ct);

                var weights = rows.Where(r => r.Category == RiskIndexCategory.Reg && r.Key.Kind == RiskIndexObjectKind.Scenario)
                    .GroupBy(r => r.Key.Ref).ToDictionary(g => g.Key, g => g.First().Weight);
                var total = ScenarioIds(rows).Count;
                var residual = LossSum(quantities, q => q.Residual, weights, total, day.P);
                var inherent = LossSum(quantities, q => q.Inherent, weights, total, day.P);

                var (portfolio, unavailable) = day.Profile.Mode != RiskIndexMode.Full
                    ? (null, RiskIndexErrors.RequiresFullMode)
                    : await PortfolioAsync(resolved, quantities, ct);
                return new RiskIndexMonetaryDto(resolved.Ref, day.Date, resolved.Provenance, total, residual, inherent, portfolio,
                    unavailable);
            });
    }

    internal sealed record ScenarioLoss(int RiskId, double? Residual, double? Inherent);

    /// <summary>
    /// Σ E[L] over the scenarios with the figure (S39 §3.2 REG-ALE): "n of N", the share of REG's weight quantified, and
    /// "partial" below <c>cobertura_quantificacao_min</c>. Means sum; a scenario without one is never read as zero.
    /// </summary>
    internal static RiskIndexLossSumDto LossSum(IReadOnlyList<ScenarioLoss> losses, Func<ScenarioLoss, double?> value,
        IReadOnlyDictionary<string, double> weights, int total, RiskIndexParameters p)
    {
        var quantified = losses.Where(l => value(l) is { } v && double.IsFinite(v)).ToList();
        var allWeight = weights.Values.Sum();
        var weight = quantified.Sum(l => weights.GetValueOrDefault(l.RiskId.ToString(CultureInfo.InvariantCulture)));
        var fraction = allWeight > 0 ? Math.Clamp(weight / allWeight, 0, 1) : 0;
        return new RiskIndexLossSumDto(quantified.Count == 0 ? null : quantified.Sum(l => value(l)!.Value), quantified.Count, total,
            fraction, quantified.Count > 0 && fraction < p.MinimumQuantificationCoverage);
    }

    /// <summary>
    /// M45's portfolio aggregation (T185) of the node's visible open scenarios that carry a quantitative analysis, with Gate B
    /// on the portfolio against the node's entity's appetite (T186). M45's limit of 500 risks is respected, never truncated.
    /// </summary>
    private async Task<(RiskIndexPortfolioTailDto? Portfolio, string? Unavailable)> PortfolioAsync(Node node,
        IReadOnlyList<ScenarioLoss> losses, CancellationToken ct)
    {
        if (tailRisk is null) return (null, RiskIndexErrors.Track9ServiceUnavailable);

        var ids = losses.Where(l => l.Inherent is not null || l.Residual is not null).Select(l => l.RiskId).Distinct().ToList();
        if (ids.Count == 0) return (null, "no_quantified_scenarios");
        if (ids.Count > TailRiskLimits.MaxPortfolioRisks) return (null, RiskIndexErrors.PortfolioTooLarge);

        // Only risks still open and visible now: a scenario closed since the snapshot would make M45 refuse the request.
        await using (var db = dalService.GetContext())
            ids = await db.Risks.AsNoTracking().Where(r => ids.Contains(r.Id) && r.Status != "Closed").Select(r => r.Id)
                .OrderBy(id => id).ToListAsync(ct);
        if (ids.Count == 0) return (null, "no_quantified_scenarios");

        try
        {
            var result = await tailRisk.AggregatePortfolioAsync(new PortfolioTailRequest { RiskIds = ids, EntityId = node.EntityId });
            if (result.Members.Count == 0) return (null, "no_quantified_scenarios");
            return (new RiskIndexPortfolioTailDto(result.ExpectedLoss, result.P95, result.P95CiLow, result.P95CiHigh,
                result.Cvar95, result.Cvar95CiLow, result.Cvar95CiHigh, result.SumOfP95, result.SumOfCvar95, result.Members.Count,
                result.NotQuantified.Count, result.Dependence.ToString(), GateBState(result.Appetite.State), result.Iterations,
                result.Seed), null);
        }
        catch (InvalidParameterException ex)
        {
            logger.Warning(ex, "The portfolio aggregation refused the node's scenarios");
            return (null, "portfolio_not_computable");
        }
    }

    internal static string GateBState(TailAppetiteState state) => RiskIndexObjectCollector.TailState(state);

    private static IReadOnlyList<int> ScenarioIds(IEnumerable<RiskIndexObjectRow> rows) =>
        rows.Where(r => r.Key.Kind == RiskIndexObjectKind.Scenario)
            .Select(r => int.TryParse(r.Key.Ref, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().Order().ToList();
}
