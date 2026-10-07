using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.TailRisk;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.Risks;
using Tools.TailRisk;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.7 (S48) — tail statistics and portfolio.
///
/// The statistics live in <c>Tools.TailRisk</c> (<see cref="TailStatisticsCalculator"/>, <see cref="TailContributions"/>,
/// <see cref="CorrelationMatrix"/>, <see cref="PortfolioAggregator"/>, <see cref="TailAppetite"/>, <see cref="TailFlag"/>),
/// pure and tested there; the runs are computed and stored by <see cref="QuantitativeRiskService"/>; this service
/// validates and writes the declared inputs (loss components, correlations, the appetite's tolerances), reads a risk's
/// tail, and aggregates portfolios on request.
///
/// How it relates to the gates (S48 §3): Gate A is untouched; Gate B on the tail is evaluated by
/// <see cref="RiskWorkflowService.EvaluateAppetiteAsync"/> beside the ordinal ceiling and enforced by
/// <see cref="RiskAcceptancesService"/> after it; the portfolio's Gate B is a reading, never a refusal.
/// </summary>
public class TailRiskService(
    ILogger logger,
    IDalService dalService,
    IQuantitativeRiskService quantitative,
    IRiskWorkflowService workflow)
    : ServiceBase(logger, dalService), ITailRiskService
{
    public const string NotPositiveSemidefiniteRule = "correlation_not_positive_semidefinite";
    public const string GroupTooLargeRule = "correlation_group_too_large";

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    // --- a risk's tail ------------------------------------------------------------------------------

    public async Task<RiskTailDto> GetRiskAsync(int riskId)
    {
        await using var db = DalService.GetContext();
        await RequireVisibleRiskAsync(db, riskId);
        return await BuildRiskTailAsync(db, riskId, recomputed: false);
    }

    public async Task<RiskTailDto> SaveLossComponentsAsync(int riskId, LossComponentsRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requested = ValidateComponents(request);

        await using (var scoped = DalService.GetContext())
            await RequireVisibleRiskAsync(scoped, riskId);

        var now = Clock();

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            // Rows are updated in place where the component already exists, so the trail shows "fine 1M → 2M" rather
            // than a delete and an insert.
            var existing = await db.RiskLossComponents.Where(c => c.RiskId == riskId).ToListAsync();

            foreach (var row in existing.Where(r => requested.All(c => c.Component != r.Component)))
                db.RiskLossComponents.Remove(row);

            foreach (var component in requested)
            {
                var row = existing.FirstOrDefault(r => r.Component == component.Component);
                if (row is null)
                {
                    row = new RiskLossComponent { RiskId = riskId, Component = component.Component, CreatedAt = now };
                    db.RiskLossComponents.Add(row);
                }
                else
                {
                    row.UpdatedAt = now;
                }

                row.LossMin = component.Min;
                row.LossMostLikely = component.MostLikely;
                row.LossMax = component.Max;
                row.Basis = component.Basis;
                row.UpdatedById = actingUserId;
            }

            await db.SaveChangesAsync();
        }

        Logger.Information("User {User} declared {Count} loss component(s) on risk {RiskId}", actingUserId,
            requested.Count, riskId);

        // The stored statistics must never describe components that no longer exist (S48 §4.4).
        var recomputed = await quantitative.RecomputeAsync(riskId) is not null;

        await using var read = DalService.GetContext();
        return await BuildRiskTailAsync(read, riskId, recomputed);
    }

    public async Task<RiskTailDto> DeleteLossComponentsAsync(int riskId, int actingUserId)
    {
        await using (var db = DalService.GetContext())
        {
            await RequireVisibleRiskAsync(db, riskId);

            db.UserId = actingUserId;
            var rows = await db.RiskLossComponents.Where(c => c.RiskId == riskId).ToListAsync();
            if (rows.Count == 0)
                throw new DataNotFoundException("risk_loss_components", riskId.ToString(CultureInfo.InvariantCulture));

            db.RiskLossComponents.RemoveRange(rows);
            await db.SaveChangesAsync();
        }

        Logger.Information("User {User} removed the loss components of risk {RiskId}; its magnitude is a single range again",
            actingUserId, riskId);

        var recomputed = await quantitative.RecomputeAsync(riskId) is not null;

        await using var read = DalService.GetContext();
        return await BuildRiskTailAsync(read, riskId, recomputed);
    }

    // --- correlations -------------------------------------------------------------------------------

    public async Task<List<RiskCorrelationDto>> GetCorrelationsAsync(int? riskId)
    {
        await using var db = DalService.GetContext();

        if (riskId is { } id) await RequireVisibleRiskAsync(db, id);

        // Through the scoped context: the query filter needs both risks visible (S48 §4.9).
        var rows = await db.RiskCorrelations.AsNoTracking()
            .Where(c => riskId == null || c.RiskAId == riskId || c.RiskBId == riskId)
            .OrderBy(c => c.RiskAId).ThenBy(c => c.RiskBId)
            .ToListAsync();

        return rows.Select(ToDto).ToList();
    }

    public async Task<RiskCorrelationDto> SaveCorrelationAsync(RiskCorrelationRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. The request on its own (400).
        if (request.RiskAId is not { } first || first <= 0)
            throw new InvalidParameterException(nameof(RiskCorrelationRequest.RiskAId), "Name the first risk of the pair.");
        if (request.RiskBId is not { } second || second <= 0)
            throw new InvalidParameterException(nameof(RiskCorrelationRequest.RiskBId), "Name the second risk of the pair.");
        if (first == second)
            throw new InvalidParameterException(nameof(RiskCorrelationRequest.RiskBId),
                "A risk is not correlated with itself; name two different risks.");

        if (request.Coefficient is not { } raw)
            throw new InvalidParameterException(nameof(RiskCorrelationRequest.Coefficient), "The coefficient is required.");
        var coefficient = Math.Round(raw, TailRiskLimits.CoefficientDecimals, MidpointRounding.AwayFromZero);
        if (coefficient is < 0 or > 1)
            throw new InvalidParameterException(nameof(RiskCorrelationRequest.Coefficient),
                "The coefficient is 0 (independent) to 1 (moving together exactly). A negative correlation is not " +
                "accepted: declared by mistake, it would understate the portfolio's tail (S48 D5).");

        var rationale = Required(request.Rationale, TailRiskLimits.MaxRationaleLength,
            nameof(RiskCorrelationRequest.Rationale),
            "Say why these scenarios move together — a common cause, the same supplier, the same asset.");

        var (a, b) = (Math.Min(first, second), Math.Max(first, second));

        // 2. Both risks visible to the caller (404 otherwise, as missing).
        await using (var scoped = DalService.GetContext())
        {
            await RequireVisibleRiskAsync(scoped, a);
            await RequireVisibleRiskAsync(scoped, b);
        }

        // 3. Validity of the correlated group, organisation-wide (S48 §4.5): positive semidefiniteness is a property of
        // the whole matrix, not of what one caller can see. The message names no risk.
        await using (var system = DalService.GetContext(withIdentity: false, bypassEntityScope: true))
        {
            var pairs = (await system.RiskCorrelations.AsNoTracking()
                    .Select(c => new { c.RiskAId, c.RiskBId, c.Coefficient })
                    .ToListAsync())
                .Where(c => !(c.RiskAId == a && c.RiskBId == b))
                .Select(c => new CorrelationPair(c.RiskAId, c.RiskBId, (double)c.Coefficient))
                .Append(new CorrelationPair(a, b, (double)coefficient))
                .ToList();

            var nodes = pairs.SelectMany(p => new[] { p.A, p.B }).Distinct();
            var group = CorrelationGroups.Of(nodes, pairs).First(g => g.Contains(a));

            if (group.Count > TailRiskLimits.MaxCorrelationGroup)
                throw new RuleBrokenException(
                    $"This correlation would join {group.Count} risks into one correlated group; the limit is " +
                    $"{TailRiskLimits.MaxCorrelationGroup}.", GroupTooLargeRule);

            if (!CorrelationMatrix.IsPositiveSemidefinite(CorrelationMatrix.Build(group, pairs)))
                throw new RuleBrokenException(
                    "With this coefficient the declared correlations would not form a valid correlation matrix (it would " +
                    "not be positive semidefinite) — for example, two scenarios that both move closely with a third " +
                    "cannot be independent of each other. Adjust this or the related declarations; nothing is repaired " +
                    "automatically.", NotPositiveSemidefiniteRule);
        }

        // 4. Write as the person.
        var now = Clock();
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var row = await db.RiskCorrelations.FirstOrDefaultAsync(c => c.RiskAId == a && c.RiskBId == b);
        if (row is null)
        {
            row = new RiskCorrelation { RiskAId = a, RiskBId = b, CreatedAt = now };
            db.RiskCorrelations.Add(row);
        }
        else
        {
            row.UpdatedAt = now;
        }

        row.Coefficient = coefficient;
        row.Rationale = rationale;
        row.UpdatedById = actingUserId;

        await db.SaveChangesAsync();

        Logger.Information("User {User} declared a correlation of {Coefficient} between risks {A} and {B}", actingUserId,
            coefficient, a, b);

        return ToDto(row);
    }

    public async Task DeleteCorrelationAsync(int correlationId, int actingUserId)
    {
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var row = await db.RiskCorrelations.FirstOrDefaultAsync(c => c.Id == correlationId)
                  ?? throw new DataNotFoundException("risk_correlations",
                      correlationId.ToString(CultureInfo.InvariantCulture));

        db.RiskCorrelations.Remove(row);
        await db.SaveChangesAsync();

        Logger.Information("User {User} removed the correlation between risks {A} and {B}; the pair is independent again",
            actingUserId, row.RiskAId, row.RiskBId);
    }

    // --- the portfolio ------------------------------------------------------------------------------

    public async Task<PortfolioTailDto> AggregatePortfolioAsync(PortfolioTailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var basis = request.Basis ?? PortfolioBasis.Residual;
        if (!Enum.IsDefined(basis))
            throw new InvalidParameterException(nameof(PortfolioTailRequest.Basis), "The basis is Residual (1) or Inherent (2).");

        if (request.RiskIds is { Count: > TailRiskLimits.MaxPortfolioRisks })
            throw new InvalidParameterException(nameof(PortfolioTailRequest.RiskIds),
                $"A portfolio request names at most {TailRiskLimits.MaxPortfolioRisks} risks.");

        var seed = request.Seed ?? TailRiskLimits.DefaultPortfolioSeed;

        await using var db = DalService.GetContext();

        if (request.EntityId is { } entity && !await db.Entities.AnyAsync(e => e.Id == entity))
            throw new InvalidParameterException(nameof(PortfolioTailRequest.EntityId), $"Entity #{entity} does not exist.");

        // The universe, through the scoped context: a risk the caller cannot see is never aggregated.
        var risks = db.Risks.AsNoTracking();
        List<UniverseRisk> universe;
        if (request.RiskIds is { } ids)
        {
            var wanted = ids.Distinct().ToList();
            universe = await risks.Where(r => wanted.Contains(r.Id))
                .Select(r => new UniverseRisk(r.Id, r.Subject, r.EntityId)).ToListAsync();

            var unknown = wanted.Except(universe.Select(r => r.Id)).OrderBy(id => id).ToList();
            if (unknown.Count > 0)
                throw new InvalidParameterException(nameof(PortfolioTailRequest.RiskIds),
                    $"Risk {string.Join(", ", unknown.Select(id => $"#{id}"))} does not exist or is outside your scope.");
        }
        else
        {
            universe = await risks
                .Where(r => r.Status != RiskWorkflowService.StatusClosed &&
                            (request.EntityId == null || r.EntityId == request.EntityId))
                .Select(r => new UniverseRisk(r.Id, r.Subject, r.EntityId)).ToListAsync();

            if (universe.Count > TailRiskLimits.MaxPortfolioRisks)
                throw new InvalidParameterException(nameof(PortfolioTailRequest.RiskIds),
                    $"{universe.Count} open risks are in scope; a portfolio aggregates at most " +
                    $"{TailRiskLimits.MaxPortfolioRisks}. Narrow it with RiskIds or EntityId.");
        }

        universe = universe.OrderBy(r => r.Id).ToList();
        var universeIds = universe.Select(r => r.Id).ToList();

        var tails = (await db.RiskTailStatistics.AsNoTracking().Include(t => t.Components)
                .Where(t => universeIds.Contains(t.RiskId)).ToListAsync())
            .GroupBy(t => t.RiskId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var members = new List<(UniverseRisk Risk, RiskTailStatistics Row)>();
        var excluded = new List<PortfolioExcludedRiskDto>();

        foreach (var risk in universe)
        {
            var rows = tails.GetValueOrDefault(risk.Id) ?? [];
            var row = basis == PortfolioBasis.Residual
                ? TailStatisticsMapping.Governing(rows)
                : rows.FirstOrDefault(r => r.Run == TailRun.Inherent);

            if (row is null)
                excluded.Add(new PortfolioExcludedRiskDto
                    { RiskId = risk.Id, Subject = risk.Subject, Reason = PortfolioExclusionReason.NotQuantified });
            else
                members.Add((risk, row));
        }

        var iterations = await QuantitativeRiskService.ReadIterationsSettingAsync(db);
        if ((long)members.Count * iterations > TailRiskLimits.MaxPortfolioSamples)
            throw new InvalidParameterException(nameof(PortfolioTailRequest.RiskIds),
                $"{members.Count} quantified risks at {iterations:N0} iterations exceed what one aggregation holds " +
                $"({TailRiskLimits.MaxPortfolioSamples:N0} samples). Narrow it with RiskIds or EntityId.");

        var memberIds = members.Select(m => m.Risk.Id).ToList();
        var declared = await db.RiskCorrelations.AsNoTracking()
            .Where(c => memberIds.Contains(c.RiskAId) && memberIds.Contains(c.RiskBId))
            .Select(c => new CorrelationPair(c.RiskAId, c.RiskBId, (double)c.Coefficient))
            .ToListAsync();

        var appetites = await db.RiskAppetites.AsNoTracking().ToListAsync();
        var appetiteIds = appetites.Select(a => a.Id).ToList();
        var limits = await db.RiskAppetiteTailLimits.AsNoTracking().Where(l => appetiteIds.Contains(l.AppetiteId))
            .ToDictionaryAsync(l => l.AppetiteId);
        var global = appetites.FirstOrDefault(a => a.EntityId == null);

        RiskAppetite? Governing(int? entityId) =>
            (entityId is not null ? appetites.FirstOrDefault(a => a.EntityId == entityId) : null) ?? global;

        var portfolioAppetite = Governing(request.EntityId);
        var portfolioLimits = portfolioAppetite is null
            ? null
            : TailStatisticsMapping.PortfolioLimits(limits.GetValueOrDefault(portfolioAppetite.Id));

        var dto = new PortfolioTailDto
        {
            GeneratedAt = Clock(),
            Basis = basis,
            Seed = seed,
            Iterations = iterations,
            ConfidenceLevel = (decimal)TailStatisticsCalculator.ConfidenceLevel,
            NotQuantified = excluded
        };

        if (members.Count == 0)
        {
            dto.Dependence = PortfolioDependence.AssumedIndependent;
            dto.Appetite = TailAppetite.EvaluatePortfolio(portfolioAppetite?.Id, portfolioAppetite?.EntityId,
                portfolioLimits, null, 0, universe.Count);
            return dto;
        }

        PortfolioAggregate aggregate;
        try
        {
            aggregate = PortfolioAggregator.Aggregate(members.Select(m => ToInput(m.Row)).ToList(), declared, iterations,
                seed, TailRiskLimits.MaxCorrelationGroup);
        }
        catch (NotPositiveSemidefiniteException)
        {
            throw new RuleBrokenException(
                "The correlations declared between these risks do not form a valid (positive semidefinite) correlation " +
                "matrix, so the portfolio cannot be aggregated. Correct the declarations; nothing is repaired " +
                "automatically.", NotPositiveSemidefiniteRule);
        }
        catch (CorrelationGroupTooLargeException ex)
        {
            throw new RuleBrokenException(ex.Message, GroupTooLargeRule);
        }

        var statistics = aggregate.Statistics;
        var results = aggregate.Members.ToDictionary(m => m.Id);

        dto.Dependence = aggregate.DeclaredPairs > 0 ? PortfolioDependence.Declared : PortfolioDependence.AssumedIndependent;
        dto.DeclaredPairs = aggregate.DeclaredPairs;
        dto.ExpectedLoss = statistics.ExpectedLoss;
        dto.ExpectedLossCiLow = statistics.ExpectedLossCiLow;
        dto.ExpectedLossCiHigh = statistics.ExpectedLossCiHigh;
        dto.P95 = statistics.P95;
        dto.P95CiLow = statistics.P95CiLow;
        dto.P95CiHigh = statistics.P95CiHigh;
        dto.Cvar95 = statistics.Cvar95;
        dto.Cvar95CiLow = statistics.Cvar95CiLow;
        dto.Cvar95CiHigh = statistics.Cvar95CiHigh;
        dto.ProbabilityOfLoss = statistics.ProbabilityOfLoss;
        dto.SumOfExpectedLoss = aggregate.SumOfExpectedLoss;
        dto.SumOfP95 = aggregate.SumOfP95;
        dto.SumOfCvar95 = aggregate.SumOfCvar95;
        dto.Diversification = Math.Max(0, aggregate.SumOfCvar95 - statistics.Cvar95);

        dto.Members = members.Select(m =>
        {
            var result = results[m.Risk.Id];
            var appetite = Governing(m.Risk.EntityId);
            var scenario = TailAppetite.EvaluateScenario(appetite?.Id, appetite?.EntityId,
                appetite is null ? null : TailStatisticsMapping.ScenarioLimits(limits.GetValueOrDefault(appetite.Id)),
                m.Row.Run, TailStatisticsMapping.ToStatistics(m.Row));

            return new PortfolioMemberDto
            {
                RiskId = m.Risk.Id,
                Subject = m.Risk.Subject,
                EntityId = m.Risk.EntityId,
                Run = m.Row.Run,
                StoredIterations = m.Row.Iterations,
                ExpectedLoss = result.ExpectedLoss,
                P95 = result.P95,
                Cvar95 = result.Cvar95,
                Cvar95Contribution = result.Cvar95Contribution,
                ScenarioAppetite = scenario.State
            };
        }).ToList();

        dto.Appetite = TailAppetite.EvaluatePortfolio(portfolioAppetite?.Id, portfolioAppetite?.EntityId, portfolioLimits,
            statistics, members.Count, universe.Count);

        Logger.Information(
            "Portfolio of {Members} quantified risk(s) of {Total} aggregated ({Dependence}, {Pairs} pair(s)): E[L] {Mean:N0}, " +
            "P95 {P95:N0} (Σ {SumP95:N0}), CVaR95 {Cvar:N0} (Σ {SumCvar:N0})", members.Count, universe.Count, dto.Dependence,
            dto.DeclaredPairs, dto.ExpectedLoss, dto.P95, dto.SumOfP95, dto.Cvar95, dto.SumOfCvar95);

        return dto;
    }

    // --- the appetite's tolerances ------------------------------------------------------------------

    public async Task<RiskAppetiteTailLimitsDto> GetAppetiteLimitsAsync(int appetiteId)
    {
        await using var db = DalService.GetContext();
        var appetite = await RequireAppetiteAsync(db, appetiteId);

        var row = await db.RiskAppetiteTailLimits.AsNoTracking().FirstOrDefaultAsync(l => l.AppetiteId == appetiteId)
                  ?? throw new DataNotFoundException("risk_appetite_tail_limits",
                      appetiteId.ToString(CultureInfo.InvariantCulture));

        return ToDto(row, appetite);
    }

    public async Task<RiskAppetiteTailLimitsDto> SaveAppetiteLimitsAsync(int appetiteId,
        RiskAppetiteTailLimitsRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        Limit(request.MaxScenarioExpectedLoss, nameof(RiskAppetiteTailLimitsRequest.MaxScenarioExpectedLoss));
        Limit(request.MaxScenarioP95, nameof(RiskAppetiteTailLimitsRequest.MaxScenarioP95));
        Limit(request.MaxScenarioCvar95, nameof(RiskAppetiteTailLimitsRequest.MaxScenarioCvar95));
        Limit(request.MaxPortfolioExpectedLoss, nameof(RiskAppetiteTailLimitsRequest.MaxPortfolioExpectedLoss));
        Limit(request.MaxPortfolioP95, nameof(RiskAppetiteTailLimitsRequest.MaxPortfolioP95));
        Limit(request.MaxPortfolioCvar95, nameof(RiskAppetiteTailLimitsRequest.MaxPortfolioCvar95));

        if (request.MaxScenarioExpectedLoss is null && request.MaxScenarioP95 is null && request.MaxScenarioCvar95 is null &&
            request.MaxPortfolioExpectedLoss is null && request.MaxPortfolioP95 is null && request.MaxPortfolioCvar95 is null)
            throw new InvalidParameterException("Limits",
                "Set at least one tolerance. To stop gating the tail, delete the limits instead.");

        var rationale = Required(request.Rationale, TailRiskLimits.MaxRationaleLength,
            nameof(RiskAppetiteTailLimitsRequest.Rationale),
            "Record the Phase 0 decision that set these limits — the minutes or the approval reference.");

        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var appetite = await RequireAppetiteAsync(db, appetiteId);

        var row = await db.RiskAppetiteTailLimits.FirstOrDefaultAsync(l => l.AppetiteId == appetiteId);
        var relaxed = row is not null && Relaxes(row, request);

        if (row is null)
        {
            row = new RiskAppetiteTailLimit { AppetiteId = appetiteId, CreatedAt = now };
            db.RiskAppetiteTailLimits.Add(row);
        }
        else
        {
            row.UpdatedAt = now;
        }

        row.MaxScenarioExpectedLoss = request.MaxScenarioExpectedLoss;
        row.MaxScenarioP95 = request.MaxScenarioP95;
        row.MaxScenarioCvar95 = request.MaxScenarioCvar95;
        row.MaxPortfolioExpectedLoss = request.MaxPortfolioExpectedLoss;
        row.MaxPortfolioP95 = request.MaxPortfolioP95;
        row.MaxPortfolioCvar95 = request.MaxPortfolioCvar95;
        row.Rationale = rationale;
        row.UpdatedById = actingUserId;

        await db.SaveChangesAsync();

        // Raising or removing a tolerance makes a previously refused acceptance possible — the act the trail exists
        // for; the interceptor records the fields, and this line makes it visible in the operational log too.
        if (relaxed)
            Logger.Warning("Risk appetite {Appetite} tail tolerance RELAXED by user {User}", appetiteId, actingUserId);
        else
            Logger.Information("Risk appetite {Appetite} tail tolerances set by user {User}", appetiteId, actingUserId);

        return ToDto(row, appetite);
    }

    public async Task DeleteAppetiteLimitsAsync(int appetiteId, int actingUserId)
    {
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireAppetiteAsync(db, appetiteId);

        var row = await db.RiskAppetiteTailLimits.FirstOrDefaultAsync(l => l.AppetiteId == appetiteId)
                  ?? throw new DataNotFoundException("risk_appetite_tail_limits",
                      appetiteId.ToString(CultureInfo.InvariantCulture));

        db.RiskAppetiteTailLimits.Remove(row);
        await db.SaveChangesAsync();

        Logger.Warning("Risk appetite {Appetite} tail tolerances REMOVED by user {User}; the tail is no longer gated there",
            appetiteId, actingUserId);
    }

    // --- flag 8 thresholds --------------------------------------------------------------------------

    /// <summary>
    /// The flag 8 thresholds in force (S48 §4.8): the two settings, each defaulting when missing or malformed, the
    /// catastrophic loss defaulting to the top quantitative band threshold. Shared with the reconciliation.
    /// </summary>
    public static async Task<TailFlagThresholds> ReadTailFlagThresholdsAsync(AuditableContext db)
    {
        var settings = await db.Settings.AsNoTracking()
            .Where(t => t.Name == TailRiskSettingKeys.TailFlagMaxAnnualProbability ||
                        t.Name == TailRiskSettingKeys.TailFlagCatastrophicLoss ||
                        t.Name == QuantitativeRiskService.BandThresholdsSetting)
            .Select(t => new { t.Name, t.Value })
            .ToListAsync();

        string? Setting(string name) => settings.FirstOrDefault(t => t.Name == name)?.Value;

        return TailFlagThresholds.Resolve(
            Setting(TailRiskSettingKeys.TailFlagMaxAnnualProbability),
            Setting(TailRiskSettingKeys.TailFlagCatastrophicLoss),
            QuantitativeRiskService.ParseBandThresholds(Setting(QuantitativeRiskService.BandThresholdsSetting))[^1]);
    }

    // --- helpers ------------------------------------------------------------------------------------

    private sealed record UniverseRisk(int Id, string Subject, int? EntityId);

    private sealed record ValidComponent(LossComponent Component, double Min, double MostLikely, double Max, string? Basis);

    private async Task<RiskTailDto> BuildRiskTailAsync(AuditableContext db, int riskId, bool recomputed)
    {
        var tails = await db.RiskTailStatistics.AsNoTracking().Include(t => t.Components)
            .Where(t => t.RiskId == riskId).ToListAsync();
        var inherent = tails.FirstOrDefault(t => t.Run == TailRun.Inherent);
        var residual = tails.FirstOrDefault(t => t.Run == TailRun.Residual);

        var declared = await db.RiskLossComponents.AsNoTracking().Where(c => c.RiskId == riskId)
            .OrderBy(c => c.Component).ToListAsync();

        var flag = await db.RiskFlags.AsNoTracking()
            .FirstOrDefaultAsync(f => f.RiskId == riskId && f.Flag == RiskFlagCode.LowProbabilityCatastrophic);

        var thresholds = await ReadTailFlagThresholdsAsync(db);
        var basis = inherent is null
            ? null
            : TailFlag.Basis(inherent.ProbabilityOfLoss, inherent.ConditionalLoss, inherent.Iterations, inherent.Seed,
                thresholds);

        var appetite = await workflow.EvaluateAppetiteAsync(riskId);

        return new RiskTailDto
        {
            RiskId = riskId,
            Inherent = inherent is null ? null : TailStatisticsMapping.ToDto(inherent),
            Residual = residual is null ? null : TailStatisticsMapping.ToDto(residual),
            DeclaredComponents = declared.Select(c => new LossComponentDto
            {
                Component = c.Component,
                Min = c.LossMin,
                MostLikely = c.LossMostLikely,
                Max = c.LossMax,
                Basis = c.Basis,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                UpdatedById = c.UpdatedById
            }).ToList(),
            Recomputed = recomputed,
            Appetite = appetite.Tail,
            TailFlag = new TailFlagCriterionDto
            {
                Holds = inherent is null ? null : basis is not null,
                Basis = basis,
                MaxAnnualProbability = thresholds.MaxAnnualProbability,
                CatastrophicLoss = thresholds.CatastrophicLoss,
                Derived = flag?.Derived ?? false,
                Declared = flag?.Declared ?? false
            }
        };
    }

    private static List<ValidComponent> ValidateComponents(LossComponentsRequest request)
    {
        var components = request.Components;
        if (components is null || components.Count == 0)
            throw new InvalidParameterException(nameof(LossComponentsRequest.Components),
                "Declare at least one loss component. To go back to a single magnitude range, delete the components.");

        var all = Enum.GetValues<LossComponent>();
        if (components.Count > all.Length)
            throw new InvalidParameterException(nameof(LossComponentsRequest.Components),
                $"There are {all.Length} forms of loss; each is declared at most once.");

        var valid = new List<ValidComponent>();

        for (var i = 0; i < components.Count; i++)
        {
            var item = components[i] ?? throw new InvalidParameterException($"Components[{i}]", "A component is required.");
            var field = $"Components[{i}]";

            if (item.Component is not { } component || !Enum.IsDefined(component))
                throw new InvalidParameterException($"{field}.Component",
                    "The component is Response (1), Recovery (2), Productivity (3), Revenue (4), Liability (5), Fine (6) " +
                    "or Reputation (7).");

            if (valid.Any(v => v.Component == component))
                throw new InvalidParameterException($"{field}.Component", $"{component} is declared twice.");

            if (item.Min is not { } min || item.MostLikely is not { } mostLikely || item.Max is not { } max ||
                !double.IsFinite(min) || !double.IsFinite(mostLikely) || !double.IsFinite(max) ||
                !new CalibratedRange(min, mostLikely, max).IsValid || max > TailRiskLimits.MaxLoss)
                throw new InvalidParameterException($"{field}.MostLikely",
                    $"The range has to run 0 ≤ minimum ≤ most likely ≤ maximum ≤ {TailRiskLimits.MaxLoss:N0}, per event.");

            var basis = string.IsNullOrWhiteSpace(item.Basis) ? null : item.Basis.Trim();
            if (basis is { Length: > TailRiskLimits.MaxBasisLength })
                throw new InvalidParameterException($"{field}.Basis",
                    $"The basis is at most {TailRiskLimits.MaxBasisLength} characters.");

            // "Fines when legally applicable" (Phase 3): a fine with no written legal basis is a guess that looks like
            // an obligation.
            if (component == LossComponent.Fine && basis is null)
                throw new InvalidParameterException($"{field}.Basis",
                    "A fine needs its legal basis — the regulation and article that makes it applicable.");

            valid.Add(new ValidComponent(component, min, mostLikely, max, basis));
        }

        return valid.OrderBy(v => v.Component).ToList();
    }

    private static PortfolioMemberInput ToInput(RiskTailStatistics row)
    {
        IReadOnlyList<CalibratedRange> magnitude = row.MagnitudeSource == MagnitudeSource.Components && row.Components.Count > 0
            ? row.Components.OrderBy(c => c.Component)
                .Select(c => new CalibratedRange(c.LossMin, c.LossMostLikely, c.LossMax)).ToList()
            : [new CalibratedRange(row.MagnitudeMin, row.MagnitudeMostLikely, row.MagnitudeMax)];

        return new PortfolioMemberInput(row.RiskId, new CalibratedRange(row.LefMin, row.LefMostLikely, row.LefMax),
            magnitude, row.MitigationEffectiveness, row.Seed);
    }

    private static bool Relaxes(RiskAppetiteTailLimit row, RiskAppetiteTailLimitsRequest request)
    {
        static bool Raised(decimal? before, decimal? after) => before is { } b && (after is null || after > b);

        return Raised(row.MaxScenarioExpectedLoss, request.MaxScenarioExpectedLoss) ||
               Raised(row.MaxScenarioP95, request.MaxScenarioP95) ||
               Raised(row.MaxScenarioCvar95, request.MaxScenarioCvar95) ||
               Raised(row.MaxPortfolioExpectedLoss, request.MaxPortfolioExpectedLoss) ||
               Raised(row.MaxPortfolioP95, request.MaxPortfolioP95) ||
               Raised(row.MaxPortfolioCvar95, request.MaxPortfolioCvar95);
    }

    private static void Limit(decimal? value, string field)
    {
        if (value is < 0 or > TailRiskLimits.MaxLimit)
            throw new InvalidParameterException(field, $"A tolerance is 0 to {TailRiskLimits.MaxLimit:N0}.");
    }

    private static string Required(string? value, int max, string field, string missing)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) throw new InvalidParameterException(field, missing);
        if (text.Length > max) throw new InvalidParameterException(field, $"At most {max} characters.");
        return text;
    }

    private static async Task RequireVisibleRiskAsync(AuditableContext db, int riskId)
    {
        if (!await db.Risks.AnyAsync(r => r.Id == riskId))
            throw new DataNotFoundException("risks", riskId.ToString(CultureInfo.InvariantCulture));
    }

    private static async Task<RiskAppetite> RequireAppetiteAsync(AuditableContext db, int appetiteId) =>
        await db.RiskAppetites.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appetiteId)
        ?? throw new DataNotFoundException("risk_appetites", appetiteId.ToString(CultureInfo.InvariantCulture));

    private static RiskCorrelationDto ToDto(RiskCorrelation row) => new()
    {
        Id = row.Id,
        RiskAId = row.RiskAId,
        RiskBId = row.RiskBId,
        Coefficient = row.Coefficient,
        Rationale = row.Rationale,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
        UpdatedById = row.UpdatedById
    };

    private static RiskAppetiteTailLimitsDto ToDto(RiskAppetiteTailLimit row, RiskAppetite appetite) => new()
    {
        AppetiteId = row.AppetiteId,
        EntityId = appetite.EntityId,
        MaxScenarioExpectedLoss = row.MaxScenarioExpectedLoss,
        MaxScenarioP95 = row.MaxScenarioP95,
        MaxScenarioCvar95 = row.MaxScenarioCvar95,
        MaxPortfolioExpectedLoss = row.MaxPortfolioExpectedLoss,
        MaxPortfolioP95 = row.MaxPortfolioP95,
        MaxPortfolioCvar95 = row.MaxPortfolioCvar95,
        Rationale = row.Rationale,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
        UpdatedById = row.UpdatedById
    };
}
