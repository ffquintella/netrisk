using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.RiskFlags;
using Model.TreatmentEconomics;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.TreatmentEconomics;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.6 (S47) — treatment economics: the four treatment options, the monetary cost beside the ordinal scale,
/// Gate C, the target risk level and Gate D.
///
/// The arithmetic lives in <c>Tools.TreatmentEconomics</c> (<see cref="TreatmentCost"/>, <see cref="GateC"/>,
/// <see cref="PortfolioSelector"/>, <see cref="TargetLevel"/>, <see cref="ActionPlanCompleteness"/>), pure and
/// tested there; this service loads, validates, enforces Gate A and writes.
///
/// How it relates to the other gates (S47 §3): Gate A comes first — "accept" is refused through the same
/// <see cref="IRiskFlagsService.EnsureGateAAllowsAsync"/> every acceptance path calls, Gate C is informational on a
/// Gate A risk, and Gate D selects Gate A treatments before anything else. Nothing here touches acceptance, so a
/// failing Gate C never makes a risk acceptable: Gate B (the appetite) is still checked where it always was.
/// </summary>
public class TreatmentEconomicsService(
    ILogger logger,
    IDalService dalService,
    IRiskFlagsService flags,
    IRiskWorkflowService workflow)
    : ServiceBase(logger, dalService), ITreatmentEconomicsService
{
    public const string DependencyCycleRule = "dependency_cycle";

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    // --- mitigation economics and Gate C ------------------------------------------------------------

    public async Task<MitigationEconomicsDto> GetMitigationAsync(int mitigationId)
    {
        await using var db = DalService.GetContext();

        var mitigation = await db.Mitigations.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mitigationId)
                         ?? throw MitigationNotFound(mitigationId);

        return (await LoadViewsAsync(db, [mitigation])).Single().Dto;
    }

    public async Task<MitigationEconomicsDto> SaveMitigationAsync(int mitigationId, MitigationEconomicsRequest request,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. The request on its own (400), before anything is read.
        if (request.Option is not { } option || !Enum.IsDefined(option))
            throw new InvalidParameterException(nameof(MitigationEconomicsRequest.Option),
                "The treatment option is Avoid (1), Reduce (2), TransferShare (3) or Accept (4).");

        var counterparty = Optional(request.TransferCounterparty, TreatmentEconomicsLimits.MaxCounterpartyLength,
            nameof(MitigationEconomicsRequest.TransferCounterparty));
        if (option == TreatmentOption.TransferShare && counterparty is null)
            throw new InvalidParameterException(nameof(MitigationEconomicsRequest.TransferCounterparty),
                "A transfer or share names its counterparty — the insurer, supplier or contract party that takes the loss.");

        var cost = request.Cost is null ? null : TreatmentCost.FromRequest(request.Cost);

        var basis = Optional(request.CostBasis, TreatmentEconomicsLimits.MaxCostBasisLength,
            nameof(MitigationEconomicsRequest.CostBasis));

        if (request.EffortPersonDays is < 0 or > TreatmentEconomicsLimits.MaxEffortPersonDays)
            throw new InvalidParameterException(nameof(MitigationEconomicsRequest.EffortPersonDays),
                $"The effort is 0 to {TreatmentEconomicsLimits.MaxEffortPersonDays:N0} person-days.");

        if (request.DurationDays is < 0 or > TreatmentEconomicsLimits.MaxDurationDays)
            throw new InvalidParameterException(nameof(MitigationEconomicsRequest.DurationDays),
                $"The duration is 0 to {TreatmentEconomicsLimits.MaxDurationDays} days.");

        var requested = (request.PrerequisiteMitigationIds ?? []).Distinct().OrderBy(id => id).ToList();
        if (requested.Count > TreatmentEconomicsLimits.MaxPrerequisites)
            throw new InvalidParameterException(nameof(MitigationEconomicsRequest.PrerequisiteMitigationIds),
                $"A treatment has at most {TreatmentEconomicsLimits.MaxPrerequisites} prerequisites.");
        if (requested.Contains(mitigationId))
            throw new InvalidParameterException(nameof(MitigationEconomicsRequest.PrerequisiteMitigationIds),
                "A treatment cannot be its own prerequisite.");

        // 2. Existence and scope (404), and prerequisites the caller can see (400).
        int riskId;
        await using (var scoped = DalService.GetContext())
        {
            riskId = await scoped.Mitigations.Where(m => m.Id == mitigationId).Select(m => (int?)m.RiskId)
                         .FirstOrDefaultAsync()
                     ?? throw MitigationNotFound(mitigationId);

            var visible = await scoped.Mitigations.Where(m => requested.Contains(m.Id)).Select(m => m.Id).ToListAsync();
            var unknown = requested.Except(visible).ToList();
            if (unknown.Count > 0)
                throw new InvalidParameterException(nameof(MitigationEconomicsRequest.PrerequisiteMitigationIds),
                    $"Mitigation {string.Join(", ", unknown.Select(id => $"#{id}"))} does not exist or is outside your scope.");
        }

        // 3. Gate A, the first merit check (S46 D7): declaring "accept" is refused exactly as accepting is.
        if (option == TreatmentOption.Accept)
            await flags.EnsureGateAAllowsAsync(riskId, GateAAction.Accept);

        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        // Dependency rows whose prerequisite this caller cannot see are preserved: a scoped caller must not be able
        // to delete a constraint it cannot see, nor re-add it.
        var existing = await db.MitigationDependencies.Where(d => d.MitigationId == mitigationId).ToListAsync();
        var existingPrerequisites = existing.Select(d => d.PrerequisiteId).ToList();
        var visiblePrerequisites = await db.Mitigations.Where(m => existingPrerequisites.Contains(m.Id))
            .Select(m => m.Id).ToListAsync();
        var preserved = existing.Where(d => !visiblePrerequisites.Contains(d.PrerequisiteId)).ToList();
        var finalPrerequisites = requested.Concat(preserved.Select(d => d.PrerequisiteId)).Distinct().ToList();

        // 4. Cycles (422), over the whole organisation's dependencies: a cycle through a mitigation the caller
        // cannot see is still a cycle. Only the verdict leaves this block.
        await EnsureNoCycleAsync(mitigationId, finalPrerequisites);

        // 5. Write.
        var row = await db.MitigationEconomics.FirstOrDefaultAsync(e => e.MitigationId == mitigationId);
        if (row is null)
        {
            row = new MitigationEconomics { MitigationId = mitigationId, CreatedAt = now };
            db.MitigationEconomics.Add(row);
        }

        row.TreatmentOption = option;
        row.TransferCounterparty = counterparty;
        row.CostOneTime = cost?.OneTime;
        row.CostAnnual = cost?.Annual;
        row.CostSideEffectsAnnual = cost?.SideEffectsAnnual;
        row.CostHorizonYears = cost?.HorizonYears;
        row.CostBasis = basis;
        row.EffortPersonDays = request.EffortPersonDays;
        row.DurationDays = request.DurationDays;
        row.UpdatedAt = now;
        row.UpdatedById = actingUserId;

        foreach (var dependency in existing.Where(d => visiblePrerequisites.Contains(d.PrerequisiteId)
                                                       && !requested.Contains(d.PrerequisiteId)))
            db.MitigationDependencies.Remove(dependency);

        foreach (var prerequisite in requested.Where(p => !existingPrerequisites.Contains(p)))
            db.MitigationDependencies.Add(new MitigationDependency
            {
                MitigationId = mitigationId, PrerequisiteId = prerequisite, CreatedAt = now, CreatedById = actingUserId
            });

        await db.SaveChangesAsync();

        Logger.Information("User {User} declared treatment economics on mitigation {Mitigation}: {Option}, cost {Cost}",
            actingUserId, mitigationId, option, cost is null ? "not declared" : "declared");

        return await GetMitigationAsync(mitigationId);
    }

    // --- the risk view and the target ---------------------------------------------------------------

    public async Task<RiskTreatmentEconomicsDto> GetRiskAsync(int riskId)
    {
        await using var db = DalService.GetContext();

        var risk = await db.Risks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == riskId)
                   ?? throw RiskNotFound(riskId);

        var mitigations = await db.Mitigations.AsNoTracking().Where(m => m.RiskId == riskId).OrderBy(m => m.Id)
            .ToListAsync();
        var views = await LoadViewsAsync(db, mitigations);

        var codes = (await SetCodesAsync(db, [riskId])).GetValueOrDefault(riskId) ?? [];
        var appetite = await workflow.EvaluateAppetiteAsync(riskId);

        return new RiskTreatmentEconomicsDto
        {
            RiskId = risk.Id,
            Subject = risk.Subject,
            Status = risk.Status,
            GateA = Tools.RiskFlags.GateA.Holds(codes),
            GateAConditions = Tools.RiskFlags.GateA.ConditionsAmong(codes),
            Systemic = codes.Contains(RiskFlagCode.SystemicSinglePointOfFailure),
            Tail = codes.Contains(RiskFlagCode.LowProbabilityCatastrophic),
            AboveAppetite = appetite.AppetiteConfigured ? appetite.ExceedsCeiling : null,
            Target = await ReadTargetAsync(db, riskId, appetite.AppetiteConfigured ? appetite.MaxAcceptableResidual : null),
            Mitigations = views.Select(v => v.Dto).ToList()
        };
    }

    public async Task<RiskTargetDto> SaveTargetAsync(int riskId, RiskTargetRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TargetScore is null && request.TargetExpectedLoss is null)
            throw new InvalidParameterException(nameof(RiskTargetRequest.TargetScore),
                "A target level is a score (0–10), an expected annual loss, or both.");

        if (request.TargetScore is < 0 or > TreatmentEconomicsLimits.MaxTargetScore)
            throw new InvalidParameterException(nameof(RiskTargetRequest.TargetScore),
                "The target score is on the residual scale, 0 to 10.");

        if (request.TargetExpectedLoss is < 0 or > TreatmentEconomicsLimits.MaxAmount)
            throw new InvalidParameterException(nameof(RiskTargetRequest.TargetExpectedLoss),
                $"The target expected loss is 0 to {TreatmentEconomicsLimits.MaxAmount:N0}.");

        var rationale = Optional(request.Rationale, TreatmentEconomicsLimits.MaxRationaleLength,
                            nameof(RiskTargetRequest.Rationale))
                        ?? throw new InvalidParameterException(nameof(RiskTargetRequest.Rationale),
                            "A target needs a written rationale: why this level, and by which treatment.");

        var now = Clock();

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            if (!await db.Risks.AnyAsync(r => r.Id == riskId)) throw RiskNotFound(riskId);

            var target = await db.RiskTargets.FirstOrDefaultAsync(t => t.RiskId == riskId);
            if (target is null)
            {
                target = new RiskTarget { RiskId = riskId, CreatedAt = now };
                db.RiskTargets.Add(target);
            }
            else
            {
                target.UpdatedAt = now;
            }

            target.TargetScore = request.TargetScore is { } score ? decimal.Round(score, 2) : null;
            target.TargetExpectedLoss = request.TargetExpectedLoss is { } loss ? decimal.Round(loss, 2) : null;
            target.TargetDate = request.TargetDate;
            target.Rationale = rationale;
            target.SetById = actingUserId;

            await db.SaveChangesAsync();
        }

        Logger.Information("User {User} set the target level of risk {RiskId}", actingUserId, riskId);

        var appetite = await workflow.EvaluateAppetiteAsync(riskId);
        await using var read = DalService.GetContext();
        return (await ReadTargetAsync(read, riskId, appetite.AppetiteConfigured ? appetite.MaxAcceptableResidual : null))!;
    }

    public async Task DeleteTargetAsync(int riskId, int actingUserId)
    {
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        if (!await db.Risks.AnyAsync(r => r.Id == riskId)) throw RiskNotFound(riskId);

        var target = await db.RiskTargets.FirstOrDefaultAsync(t => t.RiskId == riskId)
                     ?? throw new DataNotFoundException("risk_targets", riskId.ToString(CultureInfo.InvariantCulture));

        db.RiskTargets.Remove(target);
        await db.SaveChangesAsync();

        Logger.Information("User {User} removed the target level of risk {RiskId}", actingUserId, riskId);
    }

    // --- Gate D -------------------------------------------------------------------------------------

    public async Task<PortfolioSelectionDto> SelectPortfolioAsync(PortfolioSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Budget is not { } budget || budget < 0 || budget > TreatmentEconomicsLimits.MaxAmount)
            throw new InvalidParameterException(nameof(PortfolioSelectionRequest.Budget),
                $"Gate D needs a budget, 0 to {TreatmentEconomicsLimits.MaxAmount:N0}.");

        if (request.PeopleCapacityPersonDays is < 0 or > TreatmentEconomicsLimits.MaxEffortPersonDays)
            throw new InvalidParameterException(nameof(PortfolioSelectionRequest.PeopleCapacityPersonDays),
                $"The people capacity is 0 to {TreatmentEconomicsLimits.MaxEffortPersonDays:N0} person-days.");

        var now = Clock();
        var start = request.StartDate ?? DateOnly.FromDateTime(now);

        if (request.Deadline is { } deadline && deadline < start)
            throw new InvalidParameterException(nameof(PortfolioSelectionRequest.Deadline),
                "The deadline cannot be before the start date.");

        if (request.MitigationIds is { Count: > TreatmentEconomicsLimits.MaxPortfolioMitigations })
            throw new InvalidParameterException(nameof(PortfolioSelectionRequest.MitigationIds),
                $"A portfolio request names at most {TreatmentEconomicsLimits.MaxPortfolioMitigations} mitigations.");

        await using var db = DalService.GetContext();

        List<Mitigation> universe;
        if (request.MitigationIds is { } ids)
        {
            var wanted = ids.Distinct().ToList();
            universe = await db.Mitigations.AsNoTracking().Where(m => wanted.Contains(m.Id)).ToListAsync();
            var unknown = wanted.Except(universe.Select(m => m.Id)).OrderBy(id => id).ToList();
            if (unknown.Count > 0)
                throw new InvalidParameterException(nameof(PortfolioSelectionRequest.MitigationIds),
                    $"Mitigation {string.Join(", ", unknown.Select(id => $"#{id}"))} does not exist or is outside your scope.");
        }
        else
        {
            var openRisks = db.Risks.Where(r => r.Status != RiskWorkflowService.StatusClosed).Select(r => r.Id);
            universe = await db.Mitigations.AsNoTracking().Where(m => openRisks.Contains(m.RiskId)).ToListAsync();
        }

        var views = await LoadViewsAsync(db, universe);

        // Completed = at least one task and every task completed. Read for the universe and for every prerequisite
        // it names, through the scoped context: a prerequisite the caller cannot see is never reported completed.
        var prerequisiteIds = views.SelectMany(v => v.Dto.PrerequisiteMitigationIds).ToList();
        var taskSubjects = universe.Select(m => m.Id).Concat(prerequisiteIds).Distinct().ToList();
        var completed = (await db.MitigationTasks.AsNoTracking()
                .Where(t => taskSubjects.Contains(t.MitigationId))
                .Select(t => new { t.MitigationId, t.Status })
                .ToListAsync())
            .GroupBy(t => t.MitigationId)
            .Where(g => g.All(t => t.Status == MitigationTaskStatus.Completed))
            .Select(g => g.Key)
            .ToHashSet();

        var appetites = await db.RiskAppetites.AsNoTracking().ToListAsync();
        var global = appetites.FirstOrDefault(a => a.EntityId == null);
        var riskIds = universe.Select(m => m.RiskId).Distinct().ToList();
        var risks = await db.Risks.AsNoTracking().Where(r => riskIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Subject, r.EntityId }).ToDictionaryAsync(r => r.Id);
        var scores = await db.RiskScorings.AsNoTracking().Where(s => riskIds.Contains(s.Id))
            .Select(s => new { s.Id, s.ResidualRisk, s.CalculatedRisk }).ToDictionaryAsync(s => s.Id);

        bool? AboveAppetite(int riskId)
        {
            // The rule of RiskWorkflowService.EvaluateAppetiteAsync/CountRisksAboveAppetiteAsync: the entity's own
            // appetite, else the global one; residual where it exists, inherent where it does not.
            var entityId = risks.TryGetValue(riskId, out var r) ? r.EntityId : null;
            var ceiling = (entityId is not null ? appetites.FirstOrDefault(a => a.EntityId == entityId) : null)
                          ?? global;
            if (ceiling is null || !scores.TryGetValue(riskId, out var s)) return null;
            double score = s.ResidualRisk ?? s.CalculatedRisk;
            return score > ceiling.MaxAcceptableResidual;
        }

        var candidates = views
            .Where(v => !completed.Contains(v.Dto.MitigationId))
            .Select(v => new PortfolioCandidate
            {
                MitigationId = v.Dto.MitigationId,
                RiskId = v.Dto.RiskId,
                RiskSubject = risks.TryGetValue(v.Dto.RiskId, out var r) ? r.Subject : string.Empty,
                Option = v.Dto.Option,
                Cost = v.Cost,
                EffortPersonDays = v.Dto.EffortPersonDays,
                DurationDays = v.Dto.DurationDays,
                Prerequisites = v.Dto.PrerequisiteMitigationIds,
                GateC = v.Dto.GateC,
                GateA = Tools.RiskFlags.GateA.Holds(v.Codes),
                Systemic = v.Codes.Contains(RiskFlagCode.SystemicSinglePointOfFailure),
                Tail = v.Codes.Contains(RiskFlagCode.LowProbabilityCatastrophic),
                AboveAppetite = AboveAppetite(v.Dto.RiskId)
            })
            .ToList();

        var constraints = new PortfolioConstraints(budget, request.PeopleCapacityPersonDays,
            request.Deadline is { } due ? due.DayNumber - start.DayNumber : null);

        var selection = PortfolioSelector.Select(constraints, candidates, completed);
        selection.GeneratedAt = now;
        selection.StartDate = start;
        selection.Deadline = request.Deadline;

        return selection;
    }

    // --- loading ------------------------------------------------------------------------------------

    private sealed record MitigationView(MitigationEconomicsDto Dto, TreatmentCost? Cost, IReadOnlyList<RiskFlagCode> Codes);

    /// <summary>The economics, Gate C and action plan of each mitigation, in a fixed number of queries.</summary>
    private async Task<List<MitigationView>> LoadViewsAsync(AuditableContext db, IReadOnlyCollection<Mitigation> mitigations)
    {
        if (mitigations.Count == 0) return [];

        var ids = mitigations.Select(m => m.Id).ToList();
        var riskIds = mitigations.Select(m => m.RiskId).Distinct().ToList();

        var economics = await db.MitigationEconomics.AsNoTracking().Where(e => ids.Contains(e.MitigationId))
            .ToDictionaryAsync(e => e.MitigationId);

        var dependencies = (await db.MitigationDependencies.AsNoTracking().Where(d => ids.Contains(d.MitigationId))
                .Select(d => new { d.MitigationId, d.PrerequisiteId }).ToListAsync())
            .GroupBy(d => d.MitigationId)
            .ToDictionary(g => g.Key, g => g.Select(d => d.PrerequisiteId).OrderBy(p => p).ToList());

        var scorings = await db.RiskScorings.AsNoTracking().Where(s => riskIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id);

        // The mitigation the residual run was computed for: the risk's most recent, as QuantitativeRiskService picks
        // it (by last_update, then id so the choice is deterministic).
        var current = (await db.Mitigations.AsNoTracking().Where(m => riskIds.Contains(m.RiskId))
                .Select(m => new { m.Id, m.RiskId, m.LastUpdate }).ToListAsync())
            .GroupBy(m => m.RiskId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.LastUpdate).ThenByDescending(m => m.Id).First().Id);

        var codes = await SetCodesAsync(db, riskIds);

        var tasks = (await db.MitigationTasks.AsNoTracking().Where(t => ids.Contains(t.MitigationId))
                .OrderBy(t => t.Id).ToListAsync())
            .GroupBy(t => t.MitigationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var costNames = await db.MitigationCosts.AsNoTracking().ToDictionaryAsync(c => c.Value, c => c.Name);
        var strategyNames = await db.PlanningStrategies.AsNoTracking().ToDictionaryAsync(p => p.Value, p => p.Name);

        return mitigations.OrderBy(m => m.Id).Select(m =>
        {
            economics.TryGetValue(m.Id, out var row);
            scorings.TryGetValue(m.RiskId, out var scoring);
            var riskCodes = codes.GetValueOrDefault(m.RiskId) ?? [];
            var cost = TreatmentCost.FromStored(row);
            var analysis = scoring?.QuantComputedAt is not null;

            var gate = GateC.Evaluate(new GateCInput
            {
                Option = row?.TreatmentOption,
                Cost = cost,
                QuantitativeAnalysis = analysis,
                ExpectedLossBefore = analysis ? scoring!.QuantAleMean : null,
                ExpectedLossAfter = analysis ? scoring!.QuantResidualAleMean : null,
                ResidualMedianRecorded = scoring?.QuantResidualAleP50 is not null,
                ResidualIsForThisMitigation = !current.TryGetValue(m.RiskId, out var latest) || latest == m.Id,
                GateAHolds = Tools.RiskFlags.GateA.Holds(riskCodes),
                QuantComputedAt = scoring?.QuantComputedAt,
                // Indicative only: last_update is written by the client.
                Stale = analysis && m.LastUpdate > scoring!.QuantComputedAt!.Value
            });

            var dto = new MitigationEconomicsDto
            {
                MitigationId = m.Id,
                RiskId = m.RiskId,
                Declared = row is not null,
                Option = row?.TreatmentOption,
                TransferCounterparty = row?.TransferCounterparty,
                Cost = cost?.ToDto(),
                CostBasis = row?.CostBasis,
                EffortPersonDays = row?.EffortPersonDays,
                DurationDays = row?.DurationDays,
                PrerequisiteMitigationIds = dependencies.GetValueOrDefault(m.Id) ?? [],
                OrdinalCost = m.MitigationCost,
                OrdinalCostName = costNames.GetValueOrDefault(m.MitigationCost),
                PlanningStrategyName = strategyNames.GetValueOrDefault(m.PlanningStrategy),
                MitigationPercent = m.MitigationPercent,
                GateC = gate,
                ActionPlan = (tasks.GetValueOrDefault(m.Id) ?? []).Select(t => new ActionPlanTaskDto
                {
                    TaskId = t.Id,
                    Title = t.Title,
                    Status = t.Status,
                    AcceptanceCriterion = t.AcceptanceCriterion,
                    CompletionEvidence = t.CompletionEvidence,
                    CompletionEvidenceAt = t.CompletionEvidenceAt,
                    CompletionEvidenceById = t.CompletionEvidenceById,
                    Missing = ActionPlanCompleteness.Missing(t.OwnerId is not null, t.DueDate is not null,
                        t.AcceptanceCriterion, t.Status, t.CompletionEvidence)
                }).ToList(),
                UpdatedAt = row?.UpdatedAt,
                UpdatedById = row?.UpdatedById
            };

            return new MitigationView(dto, cost, riskCodes);
        }).ToList();
    }

    /// <summary>The persisted flags that are set, per risk, through the scoped context (S46 §4.2).</summary>
    private static async Task<Dictionary<int, List<RiskFlagCode>>> SetCodesAsync(AuditableContext db, List<int> riskIds) =>
        (await db.RiskFlags.AsNoTracking()
            .Where(f => riskIds.Contains(f.RiskId) && (f.Declared || f.Derived))
            .Select(f => new { f.RiskId, f.Flag })
            .ToListAsync())
        .GroupBy(f => f.RiskId)
        .ToDictionary(g => g.Key, g => g.Select(f => f.Flag).Distinct().OrderBy(f => (int)f).ToList());

    private async Task<RiskTargetDto?> ReadTargetAsync(AuditableContext db, int riskId, double? appetiteCeiling)
    {
        var target = await db.RiskTargets.AsNoTracking().FirstOrDefaultAsync(t => t.RiskId == riskId);
        if (target is null) return null;

        var scoring = await db.RiskScorings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == riskId);
        double? currentScore = scoring is null ? null : scoring.ResidualRisk ?? scoring.CalculatedRisk;
        double? currentLoss = scoring?.QuantComputedAt is null ? null : scoring.QuantResidualAleMean ?? scoring.QuantAleMean;

        return new RiskTargetDto
        {
            RiskId = riskId,
            TargetScore = target.TargetScore,
            TargetExpectedLoss = target.TargetExpectedLoss,
            TargetDate = target.TargetDate,
            Rationale = target.Rationale,
            SetById = target.SetById,
            CreatedAt = target.CreatedAt,
            UpdatedAt = target.UpdatedAt,
            Status = TargetLevel.Evaluate(target.TargetScore, target.TargetExpectedLoss, target.TargetDate,
                currentScore, currentLoss, appetiteCeiling, DateOnly.FromDateTime(Clock()))
        };
    }

    /// <summary>
    /// Refuses a prerequisite set that closes a cycle through <paramref name="mitigationId"/>. Reads every dependency
    /// unscoped, as the system: what is circular cannot depend on who asks, and only the verdict is returned.
    /// </summary>
    private async Task EnsureNoCycleAsync(int mitigationId, List<int> prerequisites)
    {
        if (prerequisites.Count == 0) return;

        await using var system = DalService.GetContext(withIdentity: false, bypassEntityScope: true);

        var edges = (await system.MitigationDependencies.AsNoTracking()
                .Where(d => d.MitigationId != mitigationId)
                .Select(d => new { d.MitigationId, d.PrerequisiteId })
                .ToListAsync())
            .GroupBy(d => d.MitigationId)
            .ToDictionary(g => g.Key, g => g.Select(d => d.PrerequisiteId).ToList());

        var seen = new HashSet<int>();
        var frontier = new Queue<int>(prerequisites);

        while (frontier.Count > 0)
        {
            var next = frontier.Dequeue();
            if (next == mitigationId)
                throw new RuleBrokenException(
                    $"Mitigation #{mitigationId} cannot depend on these prerequisites: one of them already depends on it, " +
                    "directly or through others, and a circular plan cannot be executed.", DependencyCycleRule);

            if (!seen.Add(next)) continue;
            foreach (var p in edges.GetValueOrDefault(next) ?? []) frontier.Enqueue(p);
        }
    }

    private static string? Optional(string? value, int max, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        if (trimmed.Length > max)
            throw new InvalidParameterException(parameter, $"{parameter} is at most {max} characters.");

        return trimmed;
    }

    private static DataNotFoundException MitigationNotFound(int id) =>
        new("mitigations", id.ToString(CultureInfo.InvariantCulture));

    private static DataNotFoundException RiskNotFound(int id) =>
        new("risks", id.ToString(CultureInfo.InvariantCulture));
}
