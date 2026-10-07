using System.Globalization;
using System.Text;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Continuity;
using Model.Exceptions;
using Model.RiskFlags;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.ExploitationSignals;
using Tools.Risks;
using Tools.RiskFlags;
using Tools.TailRisk;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.5 (S46) — the eleven mandatory flags, Gate A, the Phase 4 decision and the Top Risks list.
///
/// Three rules shape everything here:
/// <list type="number">
/// <item><b>Effective = declared ∨ derived</b>, and a declaration never suppresses a derivation (D2), so
/// withdrawing a declaration cannot switch Gate A off for a KEV item.</item>
/// <item><b>The derivation is persisted and reconciled</b> (D9): a derived flag that loses its basis reverts
/// to false with <c>Update</c> rows in <c>audit_logs</c> and a <c>derived_note</c> saying what was lost —
/// never silently. It reads organisation-wide and writes as the system actor, because the flags of a risk
/// cannot depend on what the person who asked can see, and the person who asked did not decide the change.</item>
/// <item><b>Gate A is the first merit check</b> of every acceptance, renewal, closure, deletion and
/// non-immediate decision (D7), always on freshly reconciled flags, and it has no break-glass (D8).</item>
/// </list>
/// </summary>
public class RiskFlagsService(
    ILogger logger,
    IDalService dalService,
    IContinuityService continuity,
    INotificationEventPublisher notifications)
    : ServiceBase(logger, dalService), IRiskFlagsService
{
    public const int MaxTopRisks = 50;
    public const int DefaultTopRisks = 10;
    public const string GateARule = "gate_a_non_discretionary";

    /// <summary>
    /// The codes whose derived half is computed (S46 §4.5) — flag 8 from the inherent tail since Stage 9.7
    /// (S48 §4.8, D7).
    /// </summary>
    public static readonly IReadOnlyList<RiskFlagCode> DerivableCodes =
    [
        RiskFlagCode.KnownExploitation, RiskFlagCode.CriticalProcessContinuity, RiskFlagCode.SensitiveData,
        RiskFlagCode.LowProbabilityCatastrophic
    ];

    private const string StatusClosed = RiskWorkflowService.StatusClosed;
    private const string BasisFoundNote = "Derived basis found.";
    private const string BasisLostPrefix = "Derived basis lost: ";

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public IReadOnlyList<RiskFlagDescriptor> GetCatalogue() => RiskFlagCatalogue.All;

    // --- reads ------------------------------------------------------------------------------------

    public async Task<RiskFlagsStateDto> GetAsync(int riskId)
    {
        await using var db = DalService.GetContext();
        await RequireVisibleRiskAsync(db, riskId);
        return await ReadStateAsync(db, riskId);
    }

    public async Task<List<RiskDecisionDto>> GetDecisionsAsync(int riskId)
    {
        await using var db = DalService.GetContext();
        await RequireVisibleRiskAsync(db, riskId);

        var decisions = await db.RiskDecisions.AsNoTracking()
            .Where(d => d.RiskId == riskId)
            .OrderByDescending(d => d.DecidedAt).ThenByDescending(d => d.Id)
            .ToListAsync();

        return decisions.Select(ToDto).ToList();
    }

    public async Task<List<FlaggedRiskDto>> GetFlaggedAsync(RiskFlagCode? code, bool? gateA)
    {
        if (code is { } c && !RiskFlagCatalogue.IsDefined(c))
            throw new InvalidParameterException("flag", "The flag is 1 to 11, or 12 for 'no legitimate acceptance'.");

        await using var db = DalService.GetContext();

        // Through the scoped context: the RiskFlag query filter follows the risk's.
        var set = (await db.RiskFlags.AsNoTracking()
                .Where(f => f.Declared || f.Derived)
                .Select(f => new { f.RiskId, f.Flag })
                .ToListAsync())
            .GroupBy(f => f.RiskId)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Flag).Distinct().OrderBy(f => (int)f).ToList());

        var selected = set
            .Where(kv => code is null || kv.Value.Contains(code.Value))
            .Where(kv => gateA is null || Tools.RiskFlags.GateA.Holds(kv.Value) == gateA.Value)
            .Select(kv => kv.Key)
            .ToList();

        if (selected.Count == 0) return [];

        var risks = await db.Risks.AsNoTracking()
            .Where(r => selected.Contains(r.Id))
            .Select(r => new { r.Id, r.ReferenceId, r.Subject, r.Status, r.EntityId })
            .OrderBy(r => r.Id)
            .ToListAsync();

        return risks.Select(r => new FlaggedRiskDto
        {
            RiskId = r.Id,
            ReferenceId = r.ReferenceId,
            Subject = r.Subject,
            Status = r.Status,
            EntityId = r.EntityId,
            Flags = set[r.Id],
            GateA = Tools.RiskFlags.GateA.Holds(set[r.Id])
        }).ToList();
    }

    // --- reconciliation ---------------------------------------------------------------------------

    public async Task<RiskFlagsStateDto> RefreshAsync(int riskId)
    {
        await using (var scoped = DalService.GetContext())
            await RequireVisibleRiskAsync(scoped, riskId);

        await RunAsync(riskId, userStep: null, actingUserId: null);

        await using var db = DalService.GetContext();
        return await ReadStateAsync(db, riskId);
    }

    public async Task<RiskFlagsRefreshSummary> RefreshAllAsync()
    {
        var summary = new RiskFlagsRefreshSummary();
        var now = Clock();

        await using var db = SystemContext(DalService.GetContext(withIdentity: false, bypassEntityScope: true));

        var risks = await db.Risks.Where(r => r.Status != StatusClosed).ToListAsync();
        if (risks.Count == 0) return summary;

        var ids = risks.Select(r => r.Id).ToHashSet();

        var rows = (await db.RiskFlags.ToListAsync())
            .Where(f => ids.Contains(f.RiskId))
            .GroupBy(f => f.RiskId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var world = await LoadWorldAsync(db, riskId: null);

        var onsets = new List<Onset>();

        foreach (var risk in risks)
        {
            if (!rows.TryGetValue(risk.Id, out var riskRows)) rows[risk.Id] = riskRows = [];

            var before = Tools.RiskFlags.GateA.Holds(SetCodes(riskRows));
            var counts = Reconcile(db, risk.Id, riskRows, world.BasesFor(risk.Id), now);

            summary.FlagsRaised += counts.Raised;
            summary.FlagsReverted += counts.Reverted;

            var onset = DetectOnset(db, risk, riskRows, before, now);
            if (onset != null) onsets.Add(onset);
        }

        summary.RisksEvaluated = risks.Count;
        summary.GateAOnsets = onsets.Count;

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();

        summary.Escalations = await EscalateAsync(db, onsets);

        Logger.Information(
            "Risk flags reconciled: {Risks} risk(s), {Raised} derived flag(s) raised, {Reverted} reverted, " +
            "{Onsets} Gate A onset(s), {Escalations} escalation(s)",
            summary.RisksEvaluated, summary.FlagsRaised, summary.FlagsReverted, summary.GateAOnsets,
            summary.Escalations);

        return summary;
    }

    // --- declarations -----------------------------------------------------------------------------

    public async Task<RiskFlagsStateDto> DeclareAsync(int riskId, RiskFlagCode code,
        RiskFlagDeclarationRequest request, int actingUserId)
    {
        RequireDefined(code);
        var reason = RequireReason(request?.Reason, RiskFlagCatalogue.MaxReasonLength, "Reason",
            "A declared flag needs a written reason: it is what an auditor reads to know why the risk carries it.");

        await using (var scoped = DalService.GetContext())
            await RequireVisibleRiskAsync(scoped, riskId);

        await RunAsync(riskId, (db, _, rows, now) =>
        {
            var row = rows.FirstOrDefault(f => f.Flag == code);

            if (row is { Declared: true })
                throw new InvalidStateTransitionException("Declared", "Declared",
                    $"Flag {Label(code)} is already declared on this risk. Withdraw it first if the reason changed.");

            if (row is null)
            {
                row = new RiskFlag { RiskId = riskId, Flag = code, CreatedAt = now };
                db.RiskFlags.Add(row);
                rows.Add(row);
            }
            else
            {
                row.UpdatedAt = now;
            }

            row.Declared = true;
            row.DeclaredReason = reason;
            row.DeclaredAt = now;
            row.DeclaredById = actingUserId;
            return Task.CompletedTask;
        }, actingUserId);

        Logger.Information("User {User} declared flag {Flag} on risk {RiskId}", actingUserId, (int)code, riskId);

        await using var read = DalService.GetContext();
        return await ReadStateAsync(read, riskId);
    }

    public async Task<RiskFlagsStateDto> WithdrawAsync(int riskId, RiskFlagCode code,
        RiskFlagWithdrawalRequest request, int actingUserId)
    {
        RequireDefined(code);
        var reason = RequireReason(request?.Reason, RiskFlagCatalogue.MaxReasonLength, "Reason",
            "Withdrawing a declared flag needs a written reason. For a Gate A condition it is the only way the " +
            "risk can become acceptable again, so the reason is what the evidence pack exports.");

        await using (var scoped = DalService.GetContext())
            await RequireVisibleRiskAsync(scoped, riskId);

        await RunAsync(riskId, async (db, risk, rows, now) =>
        {
            var row = rows.FirstOrDefault(f => f.Flag == code);

            if (row is not { Declared: true })
                throw new InvalidStateTransitionException("NotDeclared", "Withdrawn",
                    $"Flag {Label(code)} is not declared on this risk, so there is nothing to withdraw. A derived " +
                    "flag clears only when its basis does.");

            // Maker-checker for the non-discretionary codes, with no break-glass (S46 D8): the person who
            // raised, owns or manages the risk does not remove the condition that stops it being accepted.
            if (Tools.RiskFlags.GateA.IsCondition(code) && await SegregationEnabledAsync(db))
            {
                var conflicts = RiskWorkflowService.SegregationConflicts(risk, actingUserId);
                if (conflicts.Count > 0)
                {
                    Logger.Warning("Refused withdrawing Gate A flag {Flag} on risk {RiskId} by user {User}, who {Relation}",
                        (int)code, riskId, actingUserId, RiskWorkflowService.DescribeRelation(conflicts));

                    throw new RuleBrokenException(
                        $"You cannot withdraw {Label(code)} from this risk because you " +
                        $"{RiskWorkflowService.DescribeRelation(conflicts)}. A Gate A condition is removed by someone " +
                        "other than the people who raised, own or manage the risk.",
                        "segregation_of_duties");
                }
            }

            row.Declared = false;
            row.DeclaredReason = reason;
            row.DeclaredAt = now;
            row.DeclaredById = actingUserId;
            row.UpdatedAt = now;
        }, actingUserId);

        Logger.Information("User {User} withdrew flag {Flag} on risk {RiskId}", actingUserId, (int)code, riskId);

        await using var read = DalService.GetContext();
        return await ReadStateAsync(read, riskId);
    }

    // --- decisions --------------------------------------------------------------------------------

    public async Task<RiskDecisionDto> RecordDecisionAsync(int riskId, RiskDecisionRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Decision is not { } kind || !Enum.IsDefined(kind))
            throw new InvalidParameterException(nameof(RiskDecisionRequest.Decision),
                "The decision is ActImmediately (1), TreatInCycle (2), MonitorAccept (3) or Archive (4).");

        var reason = RequireReason(request.Reason, RiskFlagCatalogue.MaxDecisionReasonLength,
            nameof(RiskDecisionRequest.Reason), "A decision needs a written reason.");

        // Refreshes first in both branches, so the decision records the conditions that hold now.
        var gate = kind == RiskDecisionKind.ActImmediately
            ? await EvaluateAsync(riskId)
            : await EnsureGateAAllowsAsync(riskId, GateAAction.Decide);

        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId)
                   ?? throw new DataNotFoundException("risks", riskId.ToString(CultureInfo.InvariantCulture));

        var decision = new RiskDecision
        {
            RiskId = riskId,
            Decision = kind,
            Source = RiskDecisionSource.Declared,
            Reason = reason,
            GateAConditions = gate.Holds ? Tools.RiskFlags.GateA.Format(gate.Conditions) : null,
            DecidedAt = now,
            DecidedById = actingUserId,
            CreatedAt = now
        };

        db.RiskDecisions.Add(decision);

        if (kind == RiskDecisionKind.ActImmediately)
            FlagForReview(risk, $"Decided 'act immediately': {reason}", now);

        await db.SaveChangesAsync();

        if (kind == RiskDecisionKind.ActImmediately)
        {
            var score = await ScoreOfAsync(db, riskId);
            await notifications.RiskGateAEscalatedAsync(risk, score,
                gate.Conditions.Select(Label).ToList(), reason);

            decision.EscalatedAt = Clock();
            await db.SaveChangesAsync();
        }

        Logger.Information("User {User} recorded decision {Decision} on risk {RiskId}", actingUserId, kind, riskId);

        return ToDto(decision);
    }

    // --- Gate A -----------------------------------------------------------------------------------

    public async Task<GateAEvaluationDto> EnsureGateAAllowsAsync(int riskId, GateAAction action)
    {
        var (evaluation, holding) = await EvaluateWithBasesAsync(riskId);

        if (!evaluation.Holds) return evaluation;

        Logger.Warning("Gate A refused {Action} on risk {RiskId}: conditions {Conditions}", action, riskId,
            Tools.RiskFlags.GateA.Format(evaluation.Conditions));

        throw new RuleBrokenException(Tools.RiskFlags.GateA.Explain(action, holding), GateARule);
    }

    private async Task<GateAEvaluationDto> EvaluateAsync(int riskId) => (await EvaluateWithBasesAsync(riskId)).Evaluation;

    private async Task<(GateAEvaluationDto Evaluation, List<(RiskFlagCode Code, string Basis)> Holding)>
        EvaluateWithBasesAsync(int riskId)
    {
        await using (var scoped = DalService.GetContext())
            await RequireVisibleRiskAsync(scoped, riskId);

        var rows = await RunAsync(riskId, userStep: null, actingUserId: null);

        var holding = rows
            .Where(r => r.IsSet && Tools.RiskFlags.GateA.IsCondition(r.Flag))
            .OrderBy(r => (int)r.Flag)
            .Select(r => (r.Flag, DescribeBasis(r)))
            .ToList();

        return (Evaluation(holding.Select(h => h.Flag).ToList()), holding);
    }

    // --- Top Risks --------------------------------------------------------------------------------

    public async Task<TopRisksDto> GetTopRisksAsync(int limit)
    {
        if (limit is < 1 or > MaxTopRisks)
            throw new InvalidParameterException(nameof(limit), $"The Top Risks list holds 1 to {MaxTopRisks} risks.");

        var now = Clock();

        await using var db = DalService.GetContext();

        var open = await db.Risks.AsNoTracking()
            .Where(r => r.Status != StatusClosed)
            .Select(r => new
            {
                r.Id, r.ReferenceId, r.Subject, r.Status, r.EntityId, r.Owner, r.BusinessRank, r.EvidenceConfidence
            })
            .ToListAsync();

        var result = new TopRisksDto { GeneratedAt = now, Limit = limit, OpenRisks = open.Count };
        if (open.Count == 0) return result;

        var ids = open.Select(r => r.Id).ToList();
        var idSet = ids.ToHashSet();

        var setFlags = (await db.RiskFlags.AsNoTracking()
                .Where(f => f.Declared || f.Derived)
                .Select(f => new { f.RiskId, f.Flag })
                .ToListAsync())
            .Where(f => idSet.Contains(f.RiskId))
            .GroupBy(f => f.RiskId)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Flag).Distinct().OrderBy(f => (int)f).ToList());

        var scorings = (await db.RiskScorings.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, s.CalculatedRisk, s.ResidualRisk, s.QuantAleMean })
                .ToListAsync())
            .ToDictionary(s => s.Id);

        var decisions = (await db.RiskDecisions.AsNoTracking().ToListAsync())
            .Where(d => idSet.Contains(d.RiskId))
            .GroupBy(d => d.RiskId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.DecidedAt).ThenByDescending(d => d.Id).ToList());

        var ranked = open
            .Select(r =>
            {
                var flags = setFlags.GetValueOrDefault(r.Id) ?? [];
                var latest = decisions.GetValueOrDefault(r.Id)?.FirstOrDefault();
                scorings.TryGetValue(r.Id, out var scoring);
                double? inherent = scoring?.CalculatedRisk;
                double? residual = scoring?.ResidualRisk;

                return new
                {
                    Risk = r,
                    Flags = flags,
                    Conditions = Tools.RiskFlags.GateA.ConditionsAmong(flags),
                    Latest = latest,
                    Inherent = inherent,
                    Residual = residual,
                    Ale = scoring?.QuantAleMean,
                    Score = residual ?? inherent
                };
            })
            // S46 §4.9, D12: Gate A, act immediately, the owner's business rank, E[L]; the ordinal score
            // only breaks ties — Phase 3 forbids ordering by the qualitative triage.
            .OrderByDescending(x => x.Conditions.Count > 0)
            .ThenByDescending(x => x.Latest?.Decision == RiskDecisionKind.ActImmediately)
            .ThenBy(x => x.Risk.BusinessRank ?? int.MaxValue)
            .ThenByDescending(x => x.Ale ?? double.MinValue)
            .ThenByDescending(x => x.Score ?? double.MinValue)
            .ThenBy(x => x.Risk.Id)
            .Take(limit)
            .ToList();

        var topIds = ranked.Select(x => x.Risk.Id).ToList();

        var history = (await db.RiskScoringHistories.AsNoTracking()
                .Where(h => topIds.Contains(h.RiskId))
                .Select(h => new { h.RiskId, h.LastUpdate, h.CalculatedRisk, h.ResidualRisk })
                .ToListAsync())
            .GroupBy(h => h.RiskId)
            .ToDictionary(g => g.Key,
                g => g.Select(h => (At: h.LastUpdate, Value: (double)(h.ResidualRisk ?? h.CalculatedRisk))).ToList());

        var acceptances = (await db.RiskAcceptances.AsNoTracking()
                .Where(a => a.RiskId != null && topIds.Contains(a.RiskId.Value) &&
                            a.Status == RiskAcceptanceStatus.Active && a.ExpiresAt > now)
                .Select(a => new { RiskId = a.RiskId!.Value, a.ExpiresAt })
                .ToListAsync())
            .GroupBy(a => a.RiskId)
            .ToDictionary(g => g.Key, g => g.Max(a => a.ExpiresAt));

        var reviews = (await db.MgmtReviews.AsNoTracking()
                .Where(r => topIds.Contains(r.RiskId))
                .Select(r => new { r.RiskId, r.Id, r.SubmissionDate, r.NextReview })
                .ToListAsync())
            .GroupBy(r => r.RiskId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.SubmissionDate).ThenByDescending(r => r.Id).First());

        var tasks = (await db.MitigationTasks.AsNoTracking()
                .Where(t => (t.Status == MitigationTaskStatus.Open || t.Status == MitigationTaskStatus.InProgress) &&
                            t.DueDate != null)
                .Join(db.Mitigations.AsNoTracking(), t => t.MitigationId, m => m.Id,
                    (t, m) => new { m.RiskId, t.DueDate })
                .Where(x => topIds.Contains(x.RiskId))
                .ToListAsync())
            .GroupBy(t => t.RiskId)
            .ToDictionary(g => g.Key, g => g.Min(t => t.DueDate!.Value));

        var ownerIds = ranked.Where(x => x.Risk.Owner != null).Select(x => x.Risk.Owner!.Value).Distinct().ToList();
        var owners = await db.Users.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Value))
            .Select(u => new { u.Value, u.Name })
            .ToDictionaryAsync(u => u.Value, u => u.Name);

        var rank = 1;
        foreach (var x in ranked)
        {
            var riskDecisions = decisions.GetValueOrDefault(x.Risk.Id) ?? [];
            var gateASince = x.Conditions.Count > 0
                ? riskDecisions.FirstOrDefault(d => d.Source == RiskDecisionSource.GateA)?.DecidedAt
                : null;

            DateTime? nextReview = reviews.TryGetValue(x.Risk.Id, out var review) && review.NextReview != default
                ? DateTime.SpecifyKind(review.NextReview.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                : null;

            result.Items.Add(new TopRiskDto
            {
                Rank = rank++,
                RiskId = x.Risk.Id,
                ReferenceId = x.Risk.ReferenceId,
                Subject = x.Risk.Subject,
                Status = x.Risk.Status,
                EntityId = x.Risk.EntityId,
                OwnerId = x.Risk.Owner,
                OwnerName = x.Risk.Owner is { } owner ? owners.GetValueOrDefault(owner) : null,
                GateA = x.Conditions.Count > 0,
                GateAConditions = x.Conditions,
                Flags = x.Flags,
                Decision = x.Latest?.Decision,
                DecidedAt = x.Latest?.DecidedAt,
                Inherent = x.Inherent,
                Residual = x.Residual,
                ExpectedAnnualLoss = x.Ale,
                BusinessRank = x.Risk.BusinessRank,
                Confidence = x.Risk.EvidenceConfidence,
                Trend = RiskTrendCalculator.Compute(history.GetValueOrDefault(x.Risk.Id) ?? [], now),
                NextDecision = NextDecisionResolver.Resolve(new NextDecisionInput
                {
                    GateA = x.Conditions.Count > 0,
                    GateASince = gateASince,
                    AcceptanceExpiresAt = acceptances.TryGetValue(x.Risk.Id, out var expiry) ? expiry : null,
                    NextManagementReviewAt = nextReview,
                    EarliestOpenTaskDueAt = tasks.TryGetValue(x.Risk.Id, out var due) ? due : null,
                    HasDecision = riskDecisions.Count > 0
                }, now)
            });
        }

        return result;
    }

    // --- the reconciliation core ------------------------------------------------------------------

    private delegate Task UserStep(AuditableContext db, Risk risk, List<RiskFlag> rows, DateTime now);

    /// <summary>
    /// One risk: the optional person's change (saved under their id), then the reconciliation and any Gate A
    /// onset (saved as the system), then the escalation. Returns the risk's rows as they ended.
    /// </summary>
    private async Task<List<RiskFlag>> RunAsync(int riskId, UserStep? userStep, int? actingUserId)
    {
        var now = Clock();

        // Unscoped on purpose (D9); the caller has already proved the risk is visible to the person.
        await using var db = DalService.GetContext(withIdentity: true, bypassEntityScope: true);

        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId)
                   ?? throw new DataNotFoundException("risks", riskId.ToString(CultureInfo.InvariantCulture));

        var rows = await db.RiskFlags.Where(f => f.RiskId == riskId).ToListAsync();
        var before = Tools.RiskFlags.GateA.Holds(SetCodes(rows));

        if (userStep != null)
        {
            if (actingUserId is { } user) db.UserId = user;
            await userStep(db, risk, rows, now);
            await db.SaveChangesAsync();
        }

        SystemContext(db);

        var world = await LoadWorldAsync(db, riskId);
        Reconcile(db, riskId, rows, world.BasesFor(riskId), now);
        var onset = DetectOnset(db, risk, rows, before, now);

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();

        if (onset != null) await EscalateAsync(db, [onset]);

        return rows;
    }

    private sealed record ReconcileCounts(int Raised, int Reverted);

    private sealed record Onset(Risk Risk, RiskDecision Decision, List<RiskFlagCode> Conditions);

    /// <summary>
    /// The derived half of each derivable flag against its freshly computed basis (S46 §4.6). Writes only
    /// what changed, so a pass over an unchanged register saves nothing and leaves no trail.
    /// </summary>
    private static ReconcileCounts Reconcile(AuditableContext db, int riskId, List<RiskFlag> rows,
        IReadOnlyDictionary<RiskFlagCode, DerivedBasis> bases, DateTime now)
    {
        int raised = 0, reverted = 0;

        foreach (var code in DerivableCodes)
        {
            bases.TryGetValue(code, out var basis);
            var row = rows.FirstOrDefault(f => f.Flag == code);

            if (basis != null)
            {
                var text = RiskFlagSchema.Truncate(basis.Text, RiskFlagSchema.MaxBasisLength);

                if (row == null)
                {
                    row = new RiskFlag { RiskId = riskId, Flag = code, CreatedAt = now };
                    db.RiskFlags.Add(row);
                    rows.Add(row);
                }

                if (!row.Derived)
                {
                    row.Derived = true;
                    row.DerivedBasis = text;
                    row.DerivedWeight = basis.Weight;
                    row.DerivedChangedAt = now;
                    row.DerivedNote = BasisFoundNote;
                    if (row.Id != 0) row.UpdatedAt = now;
                    raised++;
                }
                else if (row.DerivedBasis != text || row.DerivedWeight != basis.Weight)
                {
                    row.DerivedBasis = text;
                    row.DerivedWeight = basis.Weight;
                    row.UpdatedAt = now;
                }

                continue;
            }

            if (row is not { Derived: true }) continue;

            // Never silent (T173): the flag reverts, and both the note and the trail say what was lost.
            row.DerivedNote = RiskFlagSchema.Truncate(BasisLostPrefix + (row.DerivedBasis ?? "(no basis recorded)"),
                RiskFlagSchema.MaxNoteLength);
            row.Derived = false;
            row.DerivedBasis = null;
            row.DerivedWeight = null;
            row.DerivedChangedAt = now;
            row.UpdatedAt = now;
            reverted++;
        }

        return new ReconcileCounts(raised, reverted);
    }

    /// <summary>
    /// A Gate A onset — the predicate false before this pass and true after — records the automatic
    /// "act immediately" decision and marks the risk for review in the same save (S46 §4.8).
    /// </summary>
    private static Onset? DetectOnset(AuditableContext db, Risk risk, List<RiskFlag> rows, bool before, DateTime now)
    {
        var conditions = Tools.RiskFlags.GateA.ConditionsAmong(SetCodes(rows));
        if (before || conditions.Count == 0) return null;

        var reason = new StringBuilder("Gate A condition reached: ");
        reason.Append(string.Join("; ", rows
            .Where(r => r.IsSet && Tools.RiskFlags.GateA.IsCondition(r.Flag))
            .OrderBy(r => (int)r.Flag)
            .Select(r => $"{Label(r.Flag)} — {DescribeBasis(r)}")));
        reason.Append(". Non-discretionary: act immediately.");

        var decision = new RiskDecision
        {
            RiskId = risk.Id,
            Decision = RiskDecisionKind.ActImmediately,
            Source = RiskDecisionSource.GateA,
            Reason = reason.ToString(),
            GateAConditions = Tools.RiskFlags.GateA.Format(conditions),
            DecidedAt = now,
            DecidedById = null,
            CreatedAt = now
        };

        db.RiskDecisions.Add(decision);
        FlagForReview(risk, reason.ToString(), now);

        return new Onset(risk, decision, conditions);
    }

    /// <summary>One notification per onset, after the save that recorded it; then the time it was raised.</summary>
    private async Task<int> EscalateAsync(AuditableContext db, List<Onset> onsets)
    {
        if (onsets.Count == 0) return 0;

        foreach (var onset in onsets)
        {
            var score = await ScoreOfAsync(db, onset.Risk.Id);

            await notifications.RiskGateAEscalatedAsync(onset.Risk, score,
                onset.Conditions.Select(Label).ToList(), onset.Decision.Reason);

            onset.Decision.EscalatedAt = Clock();

            Logger.Warning("Gate A onset on risk {RiskId} ({Conditions}): escalated", onset.Risk.Id,
                Tools.RiskFlags.GateA.Format(onset.Conditions));
        }

        await db.SaveChangesAsync();
        return onsets.Count;
    }

    // --- the derivation world ---------------------------------------------------------------------

    private sealed record DerivedBasis(string Text, decimal? Weight);

    private sealed class DerivationWorld
    {
        public Dictionary<int, List<(int FindingId, string Cve)>> KevByRisk { get; } = new();
        public Dictionary<int, HashSet<int>> LinkedEntitiesByRisk { get; } = new();
        public List<CriticalProcessThreatDto> Threats { get; set; } = [];
        public Dictionary<int, (string? Name, int? LevelId)> Data { get; } = new();
        public Dictionary<int, (string? Name, bool Sensitive)> Levels { get; } = new();

        /// <summary>Stage 9.7: the inherent tail of each risk — probability of loss, loss-year mean, iterations, seed.</summary>
        public Dictionary<int, (double Probability, double? Conditional, int Iterations, int Seed)> InherentTail { get; } = new();

        public TailFlagThresholds TailThresholds { get; set; } =
            new(TailFlagThresholds.DefaultMaxAnnualProbability, QuantitativeRiskService.DefaultBandThresholds[^1]);

        public Dictionary<RiskFlagCode, DerivedBasis> BasesFor(int riskId)
        {
            var bases = new Dictionary<RiskFlagCode, DerivedBasis>();

            if (KevByRisk.TryGetValue(riskId, out var kev) && kev.Count > 0)
                bases[RiskFlagCode.KnownExploitation] = new DerivedBasis(string.Join("; ", kev
                    .OrderBy(k => k.Cve, StringComparer.Ordinal).ThenBy(k => k.FindingId)
                    .Select(k => $"KEV {k.Cve} on finding #{k.FindingId}")), null);

            // Flag 8 (S48 §4.8): a rare loss year that is catastrophic when it comes, on the inherent run.
            if (InherentTail.TryGetValue(riskId, out var tail) &&
                TailFlag.Basis(tail.Probability, tail.Conditional, tail.Iterations, tail.Seed, TailThresholds) is { } tailBasis)
                bases[RiskFlagCode.LowProbabilityCatastrophic] = new DerivedBasis(tailBasis, null);

            var linked = LinkedEntitiesByRisk.GetValueOrDefault(riskId);
            if (linked is null || linked.Count == 0) return bases;

            var threatened = Threats
                .Where(t => linked.Contains(t.EntityId) || t.ProviderEntityIds.Any(linked.Contains))
                .OrderByDescending(t => t.ThreatWeight).ThenBy(t => t.EntityId)
                .ToList();

            if (threatened.Count > 0)
                bases[RiskFlagCode.CriticalProcessContinuity] = new DerivedBasis(string.Join("; ", threatened
                    .Select(t => $"critical process '{t.Name}' (#{t.EntityId}), threat weight " +
                                 $"{t.ThreatWeight.ToString("0.0#", CultureInfo.InvariantCulture)} " +
                                 $"({(t.Confirmed ? "confirmed" : "unverified")})")),
                    threatened.Max(t => t.ThreatWeight));

            var sensitive = linked
                .Where(Data.ContainsKey)
                .Select(id => (Id: id, Data: Data[id]))
                .Where(d => d.Data.LevelId is { } level && Levels.TryGetValue(level, out var l) && l.Sensitive)
                .OrderBy(d => d.Id)
                .ToList();

            if (sensitive.Count > 0)
                bases[RiskFlagCode.SensitiveData] = new DerivedBasis(string.Join("; ", sensitive
                    .Select(d => $"data '{d.Data.Name}' (#{d.Id}) classified '{Levels[d.Data.LevelId!.Value].Name}' " +
                                 $"(#{d.Data.LevelId}), sensitive")), null);

            return bases;
        }
    }

    /// <summary>
    /// Everything the three derivations read, for one risk or for every open risk, through
    /// <paramref name="db"/> — an unscoped context (D9).
    /// </summary>
    private async Task<DerivationWorld> LoadWorldAsync(AuditableContext db, int? riskId)
    {
        var world = new DerivationWorld();

        // Flag 3 — the open findings linked to the risk(s) and their CVEs listed in KEV.
        var risks = riskId is { } id
            ? db.Risks.AsNoTracking().Where(r => r.Id == id)
            : db.Risks.AsNoTracking().Where(r => r.Status != StatusClosed);

        var findings = await risks
            .SelectMany(r => r.Vulnerabilities
                .Where(v => v.LifecycleStatus == FindingStatus.Active || v.LifecycleStatus == FindingStatus.Verified)
                .Select(v => new { RiskId = r.Id, FindingId = v.Id, v.Cves }))
            .ToListAsync();

        var cvesByFinding = findings
            .Select(f => (f.RiskId, f.FindingId, Cves: CveIds.Parse(f.Cves)))
            .Where(f => f.Cves.Count > 0)
            .ToList();

        var allCves = cvesByFinding.SelectMany(f => f.Cves).Distinct(StringComparer.Ordinal).ToList();

        var listed = allCves.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await db.KevEntries.AsNoTracking()
                .Where(k => k.DelistedAt == null && allCves.Contains(k.CveId))
                .Select(k => k.CveId)
                .ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var finding in cvesByFinding)
        foreach (var cve in finding.Cves.Where(listed.Contains))
        {
            if (!world.KevByRisk.TryGetValue(finding.RiskId, out var list)) world.KevByRisk[finding.RiskId] = list = [];
            if (!list.Contains((finding.FindingId, cve))) list.Add((finding.FindingId, cve));
        }

        // Flag 8 (S48 §4.8) — the inherent tail statistics and the two thresholds, read before the early returns below.
        var tails = riskId is { } tailRisk
            ? db.RiskTailStatistics.AsNoTracking().Where(t => t.RiskId == tailRisk && t.Run == TailRun.Inherent)
            : db.RiskTailStatistics.AsNoTracking().Where(t => t.Run == TailRun.Inherent && risks.Any(r => r.Id == t.RiskId));

        foreach (var tail in await tails
                     .Select(t => new { t.RiskId, t.ProbabilityOfLoss, t.ConditionalLoss, t.Iterations, t.Seed })
                     .ToListAsync())
            world.InherentTail[tail.RiskId] = (tail.ProbabilityOfLoss, tail.ConditionalLoss, tail.Iterations, tail.Seed);

        if (world.InherentTail.Count > 0) world.TailThresholds = await TailRiskService.ReadTailFlagThresholdsAsync(db);

        // Flags 4 and 5 — the entity targets of the risk's chain links.
        var links = riskId is { } one
            ? db.RiskChainLinks.AsNoTracking().Where(l => l.RiskId == one && l.EntityId != null)
            : db.RiskChainLinks.AsNoTracking().Where(l => l.EntityId != null);

        foreach (var link in await links.Select(l => new { l.RiskId, EntityId = l.EntityId!.Value }).ToListAsync())
        {
            if (!world.LinkedEntitiesByRisk.TryGetValue(link.RiskId, out var set))
                world.LinkedEntitiesByRisk[link.RiskId] = set = [];
            set.Add(link.EntityId);
        }

        if (world.LinkedEntitiesByRisk.Count == 0) return world;

        world.Threats = await continuity.GetCriticalProcessThreatsAsync();

        var linkedIds = world.LinkedEntitiesByRisk.Values.SelectMany(s => s).Distinct().ToList();

        var dataNodes = await db.Entities.AsNoTracking()
            .Where(e => linkedIds.Contains(e.Id) && e.DefinitionName == RiskChainSchema.DataDefinition)
            .Select(e => e.Id)
            .ToListAsync();

        if (dataNodes.Count == 0) return world;

        var dataProperties = await db.EntitiesProperties.AsNoTracking()
            .Where(p => dataNodes.Contains(p.Entity) &&
                        (p.Type == RiskChainSchema.NameProperty || p.Type == RiskFlagSchema.SecurityClassificationProperty))
            .Select(p => new { p.Entity, p.Type, p.Value })
            .ToListAsync();

        foreach (var node in dataNodes)
        {
            var name = dataProperties.FirstOrDefault(p => p.Entity == node && p.Type == RiskChainSchema.NameProperty)?.Value;
            var raw = dataProperties.FirstOrDefault(p => p.Entity == node &&
                                                         p.Type == RiskFlagSchema.SecurityClassificationProperty)?.Value;
            world.Data[node] = (name, int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
                ? level
                : null);
        }

        var levelIds = world.Data.Values.Where(d => d.LevelId != null).Select(d => d.LevelId!.Value).Distinct().ToList();
        if (levelIds.Count == 0) return world;

        var levelProperties = await db.EntitiesProperties.AsNoTracking()
            .Where(p => levelIds.Contains(p.Entity) &&
                        p.EntityNavigation.DefinitionName == RiskFlagSchema.SecurityClassificationLevelDefinition &&
                        (p.Type == RiskChainSchema.NameProperty || p.Type == RiskFlagSchema.SensitiveProperty))
            .Select(p => new { p.Entity, p.Type, p.Value })
            .ToListAsync();

        foreach (var level in levelIds)
        {
            var name = levelProperties.FirstOrDefault(p => p.Entity == level && p.Type == RiskChainSchema.NameProperty)?.Value;
            var raw = levelProperties.FirstOrDefault(p => p.Entity == level && p.Type == RiskFlagSchema.SensitiveProperty)?.Value;
            world.Levels[level] = (name, bool.TryParse(raw?.Trim(), out var sensitive) && sensitive);
        }

        return world;
    }

    // --- helpers ----------------------------------------------------------------------------------

    private static AuditableContext SystemContext(AuditableContext db)
    {
        db.UserId = 0;
        db.AuditActor = AuditableContext.SystemActor;
        return db;
    }

    private static async Task RequireVisibleRiskAsync(AuditableContext db, int riskId)
    {
        if (!await db.Risks.AnyAsync(r => r.Id == riskId))
            throw new DataNotFoundException("risks", riskId.ToString(CultureInfo.InvariantCulture));
    }

    private static void RequireDefined(RiskFlagCode code)
    {
        if (!RiskFlagCatalogue.IsDefined(code))
            throw new InvalidParameterException("code", "The flag is 1 to 11, or 12 for 'no legitimate acceptance'.");
    }

    private static string RequireReason(string? reason, int max, string parameter, string missing)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidParameterException(parameter, missing);

        var trimmed = reason.Trim();
        if (trimmed.Length > max)
            throw new InvalidParameterException(parameter, $"The reason is at most {max} characters.");

        return trimmed;
    }

    private static async Task<bool> SegregationEnabledAsync(AuditableContext db)
    {
        var setting = await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == RiskWorkflowService.SegregationSetting);

        // A missing row keeps the control, as RiskWorkflowService reads it.
        return setting?.Value is null || setting.Value.Trim().ToLowerInvariant() is "true" or "1" or "yes";
    }

    private static IEnumerable<RiskFlagCode> SetCodes(IEnumerable<RiskFlag> rows) =>
        rows.Where(r => r.IsSet).Select(r => r.Flag);

    private static GateAEvaluationDto Evaluation(List<RiskFlagCode> conditions) => new()
    {
        Holds = conditions.Count > 0,
        Conditions = conditions,
        Explanation = conditions.Count == 0
            ? "No Gate A condition holds."
            : $"Gate A holds: {string.Join("; ", conditions.Select(Label))}. The risk must be acted on immediately."
    };

    private static string DescribeBasis(RiskFlag row) => (row.Declared, row.Derived) switch
    {
        (true, true) => $"declared: {row.DeclaredReason}; derived: {row.DerivedBasis}",
        (true, false) => $"declared: {row.DeclaredReason}",
        (false, true) => $"derived: {row.DerivedBasis}",
        _ => string.Empty
    };

    private static string Label(RiskFlagCode code)
    {
        var descriptor = RiskFlagCatalogue.Find(code);
        return descriptor?.Number is { } number ? $"flag {number} ({descriptor.Name})" : descriptor?.Name ?? code.ToString();
    }

    private static void FlagForReview(Risk risk, string reason, DateTime now)
    {
        risk.ReviewRequested = true;
        risk.ReviewRequestedAt = now;
        risk.ReviewRequestedReason = reason;
        risk.LastUpdate = now;
    }

    private static async Task<double?> ScoreOfAsync(AuditableContext db, int riskId)
    {
        var scoring = await db.RiskScorings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == riskId);
        return scoring is null ? null : scoring.ResidualRisk ?? scoring.CalculatedRisk;
    }

    private static async Task<RiskFlagsStateDto> ReadStateAsync(AuditableContext db, int riskId)
    {
        var rows = await db.RiskFlags.AsNoTracking().Where(f => f.RiskId == riskId).ToListAsync();

        var latest = await db.RiskDecisions.AsNoTracking()
            .Where(d => d.RiskId == riskId)
            .OrderByDescending(d => d.DecidedAt).ThenByDescending(d => d.Id)
            .FirstOrDefaultAsync();

        RiskFlagStateDto State(RiskFlagDescriptor descriptor)
        {
            var row = rows.FirstOrDefault(r => r.Flag == descriptor.Code);
            return new RiskFlagStateDto
            {
                Code = descriptor.Code,
                Number = descriptor.Number,
                Name = descriptor.Name,
                IsSet = row?.IsSet ?? false,
                Declared = row?.Declared ?? false,
                DeclaredReason = row?.DeclaredReason,
                DeclaredAt = row?.DeclaredAt,
                DeclaredById = row?.DeclaredById,
                Derived = row?.Derived ?? false,
                DerivedBasis = row?.DerivedBasis,
                DerivedWeight = row?.DerivedWeight,
                DerivedChangedAt = row?.DerivedChangedAt,
                DerivedNote = row?.DerivedNote,
                Derivation = descriptor.Derivation,
                NonDiscretionary = descriptor.NonDiscretionary
            };
        }

        return new RiskFlagsStateDto
        {
            RiskId = riskId,
            Flags = RiskFlagCatalogue.Flags.Select(State).ToList(),
            NoLegitimateAcceptance = State(RiskFlagCatalogue.NoLegitimateAcceptance),
            GateA = Evaluation(Tools.RiskFlags.GateA.ConditionsAmong(SetCodes(rows))),
            CurrentDecision = latest is null ? null : ToDto(latest)
        };
    }

    private static RiskDecisionDto ToDto(RiskDecision d) => new()
    {
        Id = d.Id,
        RiskId = d.RiskId,
        Decision = d.Decision,
        Source = d.Source,
        Reason = d.Reason,
        GateAConditions = Tools.RiskFlags.GateA.Parse(d.GateAConditions),
        DecidedAt = d.DecidedAt,
        DecidedById = d.DecidedById,
        EscalatedAt = d.EscalatedAt
    };
}
