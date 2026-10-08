using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.AiGovernance;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.AiGovernance;
using Tools.DataCatalogue;
using Tools.Risks;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.12 (S53) — AI governance: the model inventory of MIGR-TI/IA Phase 6 (purpose, data, vendor, version), the
/// register's risks of an AI component with flag 11 derived from the inventory, and the model metrics of Phase 7 (accuracy,
/// precision, recall, calibration, drift and the human override rate), each with an explicit "not evaluated".
///
/// The rules are pure, in <c>Tools.AiGovernance</c>: what a model must report and the state of each metric
/// (<see cref="AiModelEvaluator"/>, where a model with no recorded evaluation is never evaluated) and the findings
/// (<see cref="AiModelFindingsEvaluator"/>). This class loads, guards and stores.
///
/// <b>Governance, never use</b> (S53 D1, T215). Nothing here runs a model or takes its output as a decision: the
/// constructor takes the logger and the data access only — none of the services that accept, review, approve, vote or
/// close — and a model is not a principal. The prohibitions of Phase 6 stay where they were, in the guards of those
/// services and in the valid-user requirement of every API policy.
///
/// <b>Scope.</b> Reads go through the caller's scoped context: a model is seen when it is the organization's
/// (<c>entity_id</c> null) or its entity is in scope, and what hangs off it follows it. A write needs the model's own entity
/// in scope (<see cref="RequireWritable"/>) and, for a new or moved model, its destination too (the context's write guard).
/// The counts a finding reads — the risks linked to a model — are taken unscoped, so a finding never depends on who asks.
///
/// <b>Logs</b> carry ids, codes and counts — never a model's purpose, an override's text or a reason.
/// </summary>
public class AiGovernanceService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IAiGovernanceService
{
    public const string RetiredRule = "ai_model_retired";
    public const string DataTargetRule = "ai_model_data_target";
    public const string ReadingVoidedRule = "ai_model_reading_voided";
    public const string OverrideVoidedRule = "ai_model_override_voided";

    /// <summary>
    /// The audited types of Stage 9.12. Their history is served by <c>GET /AiModels/{id}/History</c> after the model is found
    /// visible, and a risk's links by the risk's trail; the generic audit reader refuses them (S53 §4.8), as it refuses the
    /// third-party and catalogue types.
    /// </summary>
    public static readonly IReadOnlyCollection<string> TrailTypes =
    [
        nameof(AiModel), nameof(AiModelDataLink), nameof(AiModelMetricReading), nameof(AiModelOverride), nameof(AiModelRisk)
    ];

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    // --- reads -------------------------------------------------------------------------------------------------------

    public async Task<List<AiModelSummaryDto>> GetModelsAsync(AiModelStatus? status, bool includeRetired,
        bool withFindingsOnly)
    {
        if (status is { } s && !Enum.IsDefined(s))
            throw new InvalidParameterException(nameof(status), "The status is 1 (proposed) to 4 (retired).");

        await using var db = DalService.GetContext();

        var ids = await db.AiModels.AsNoTracking()
            .Where(m => status == null || m.Status == status)
            .Where(m => includeRetired || status == AiModelStatus.Retired || m.Status != AiModelStatus.Retired)
            .Select(m => m.Id)
            .ToListAsync();

        var dtos = await ComposeAsync(db, ids);

        return dtos
            .Where(d => !withFindingsOnly || d.Findings.Count > 0)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id)
            .Select(d => new AiModelSummaryDto
            {
                Id = d.Id, Name = d.Name, Kind = d.Kind, Source = d.Source, Version = d.Version, Status = d.Status,
                RiskTier = d.RiskTier, EntityId = d.EntityId, OwnerId = d.OwnerId, EvaluationState = d.Evaluation.State,
                LinkedRiskCount = d.Risks.Count + d.HiddenRiskCount,
                FindingCodes = d.Findings.Select(f => f.Code).ToList()
            })
            .ToList();
    }

    public async Task<AiModelDto> GetModelAsync(int modelId)
    {
        await using var db = DalService.GetContext();
        await RequireVisibleAsync(db, modelId);

        return (await ComposeAsync(db, [modelId])).Single();
    }

    public async Task<List<AuditLog>> GetHistoryAsync(int modelId, int limit)
    {
        RequireLimit(limit);

        await using var db = DalService.GetContext();
        await RequireVisibleAsync(db, modelId);

        // What hangs off it now. A risk link is found only when the reader sees the risk too; a removed data link or an
        // unlinked risk stays in audit_logs under its own id, which nothing here can find any more (S51 R5, S53 R7).
        var dataLinks = await db.AiModelDataLinks.Where(l => l.ModelId == modelId).Select(l => l.Id).ToListAsync();
        var readings = await db.AiModelMetricReadings.Where(r => r.ModelId == modelId).Select(r => r.Id).ToListAsync();
        var overrides = await db.AiModelOverrides.Where(o => o.ModelId == modelId).Select(o => o.Id).ToListAsync();
        var riskLinks = await db.AiModelRisks.Where(l => l.ModelId == modelId).Select(l => l.Id).ToListAsync();

        return await db.AuditLogs
            .Where(a =>
                (a.EntityType == nameof(AiModel) && a.EntityId == modelId) ||
                (a.EntityType == nameof(AiModelDataLink) && dataLinks.Contains(a.EntityId)) ||
                (a.EntityType == nameof(AiModelMetricReading) && readings.Contains(a.EntityId)) ||
                (a.EntityType == nameof(AiModelOverride) && overrides.Contains(a.EntityId)) ||
                (a.EntityType == nameof(AiModelRisk) && riskLinks.Contains(a.EntityId)))
            .Include(a => a.User)
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<AiModelReadingDto>> GetReadingsAsync(int modelId, AiModelMetric? metric, bool includeVoided)
    {
        if (metric is { } m && !Enum.IsDefined(m))
            throw new InvalidParameterException(nameof(metric), "The metric is 1 (accuracy) to 6 (human override rate).");

        await using var db = DalService.GetContext();
        await RequireVisibleAsync(db, modelId);

        return (await db.AiModelMetricReadings.AsNoTracking()
                .Where(r => r.ModelId == modelId && (metric == null || r.Metric == metric))
                .Where(r => includeVoided || r.VoidedAt == null)
                .ToListAsync())
            .OrderByDescending(r => r.MeasuredAt).ThenByDescending(r => r.Id)
            .Select(ToDto)
            .ToList();
    }

    public async Task<List<AiModelOverrideDto>> GetOverridesAsync(int modelId, bool includeVoided)
    {
        await using var db = DalService.GetContext();
        await RequireVisibleAsync(db, modelId);

        return (await db.AiModelOverrides.AsNoTracking()
                .Where(o => o.ModelId == modelId && (includeVoided || o.VoidedAt == null))
                .ToListAsync())
            .OrderByDescending(o => o.OccurredAt).ThenByDescending(o => o.Id)
            .Select(ToDto)
            .ToList();
    }

    public async Task<RiskAiModelsDto> GetRiskModelsAsync(int riskId)
    {
        await using var db = DalService.GetContext();
        await RequireVisibleRiskAsync(db, riskId);

        return await ComposeRiskViewAsync(db, riskId);
    }

    public async Task<AiModelMetricsSummaryDto> GetMetricsSummaryAsync()
    {
        var now = Clock();
        await using var db = DalService.GetContext();

        var ids = await db.AiModels.AsNoTracking()
            .Where(m => m.Status == AiModelStatus.Pilot || m.Status == AiModelStatus.Production)
            .Select(m => m.Id)
            .ToListAsync();

        var models = await ComposeAsync(db, ids);
        var since = now.AddDays(-90);

        return new AiModelMetricsSummaryDto
        {
            InUse = models.Count,
            Evaluated = models.Count(m => m.Evaluation.State == AiModelEvaluationState.Evaluated),
            Incomplete = models.Count(m => m.Evaluation.State == AiModelEvaluationState.Incomplete),
            Stale = models.Count(m => m.Evaluation.State == AiModelEvaluationState.Stale),
            NotEvaluated = models.Count(m => m.Evaluation.State == AiModelEvaluationState.NotEvaluated),
            EvaluatedByMetric = AiModelEvaluator.AllMetrics.Select(metric => new AiMetricCoverageDto
            {
                Metric = metric,
                Required = models.Count(m => m.Evaluation.RequiredMetrics.Contains(metric)),
                Evaluated = models.Count(m => m.Evaluation.Metrics
                    .Any(x => x.Metric == metric && x.State == AiMetricState.Evaluated))
            }).ToList(),
            OverridesLast90Days = await db.AiModelOverrides.AsNoTracking()
                .CountAsync(o => ids.Contains(o.ModelId) && o.VoidedAt == null && o.OccurredAt >= since),
            IsScopeRestricted = !DalService.GetCurrentEntityScope().IsUnrestricted
        };
    }

    // --- writes: the inventory (T212) --------------------------------------------------------------------------------

    public async Task<AiModelDto> CreateAsync(AiModelRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = Clock();
        var valid = Validate(request, now);

        int id;
        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            await RequireEntityAsync(db, valid.EntityId);
            await RequireOwnerAsync(db, valid.OwnerId);
            await RequireVendorAsync(db, valid.ThirdPartyId);
            await RequireUniqueNameAsync(valid.Name!, null);

            // No declared start: the registered version's earlier evaluations count (S53 D3).
            var model = new AiModel { CreatedAt = now, CreatedById = actingUserId, VersionSince = valid.VersionSince };
            Apply(model, valid, valid.Status ?? AiModelStatus.Proposed);
            db.AiModels.Add(model);

            // A scoped caller filing the organization's model, or another entity's, is refused by the write guard (403).
            await SaveOrConflictAsync(db, valid.Name!);
            id = model.Id;
        }

        Logger.Information("AI model {Id} registered by user {User} ({Status})", id, actingUserId,
            valid.Status ?? AiModelStatus.Proposed);
        return await GetModelAsync(id);
    }

    public async Task<AiModelDto> UpdateAsync(int modelId, AiModelRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = Clock();
        var valid = Validate(request, now);

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var model = await db.AiModels.FirstOrDefaultAsync(m => m.Id == modelId) ?? throw NotFound(modelId);

            // The write guard checks where the record goes; this checks where it is — without it a scoped caller could
            // re-file the organization's model under their own unit and so take it over (the S51 §4.10 precedent).
            RequireWritable(db, model);
            RefuseRetired(model);

            await RequireEntityAsync(db, valid.EntityId);
            // Checked when they change: an owner since disabled, or a vendor the caller cannot see, must not block every
            // other edit.
            if (valid.OwnerId != model.OwnerId) await RequireOwnerAsync(db, valid.OwnerId);
            if (valid.ThirdPartyId != model.ThirdPartyId) await RequireVendorAsync(db, valid.ThirdPartyId);
            await RequireUniqueNameAsync(valid.Name!, modelId);

            var previous = (model.Status, model.Version);
            var versionChanged = !string.Equals(previous.Version, valid.Version, StringComparison.Ordinal);

            // A new version starts after the one it replaces: a client echoing the previous start back would otherwise let
            // the old version's period evaluate the new one.
            if (versionChanged && valid.VersionSince is { } declared && model.VersionSince is { } current && declared <= current)
                throw new InvalidParameterException(nameof(AiModelRequest.VersionSince),
                    $"A new version is in use after the one it replaces, which is in use since {current:yyyy-MM-dd HH:mm} UTC.");

            Apply(model, valid, valid.Status ?? model.Status);
            // A new version is in use from now unless declared otherwise: its evaluation starts again, and nothing measured
            // before it can evaluate it (S53 D3). An unchanged version keeps its start unless a correction is declared.
            model.VersionSince = valid.VersionSince ?? (versionChanged ? now : model.VersionSince);
            model.UpdatedAt = now;
            model.UpdatedById = actingUserId;

            await SaveOrConflictAsync(db, valid.Name!);

            if (previous.Status != model.Status)
                Logger.Warning("AI model {Id} moved from {Old} to {New} by user {User}", modelId, previous.Status,
                    model.Status, actingUserId);
            if (versionChanged)
                Logger.Information("AI model {Id} changed version by user {User}: its evaluation starts again", modelId,
                    actingUserId);
        }

        return await GetModelAsync(modelId);
    }

    public async Task<AiModelDto> RetireAsync(int modelId, AiGovernanceReasonRequest request, int actingUserId)
    {
        var reason = Reason(request?.Reason, AiGovernanceLimits.MaxVoidOrRetireReasonLength,
            "A retirement needs a written reason: the inventory keeps the model as evidence, and why it left use is part of it.");

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var model = await db.AiModels.FirstOrDefaultAsync(m => m.Id == modelId) ?? throw NotFound(modelId);
            RequireWritable(db, model);
            RefuseRetired(model);

            var now = Clock();
            model.Status = AiModelStatus.Retired;
            model.RetiredAt = now;
            model.RetiredById = actingUserId;
            model.RetireReason = reason;
            model.UpdatedAt = now;
            model.UpdatedById = actingUserId;

            await db.SaveChangesAsync();
        }

        Logger.Warning("AI model {Id} retired by user {User}", modelId, actingUserId);
        return await GetModelAsync(modelId);
    }

    public async Task<AiModelDto> SetDataAsync(int modelId, AiModelDataRequest request, int actingUserId)
    {
        var wanted = ValidateData(request?.Data);

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var model = await db.AiModels.Include(m => m.DataLinks).FirstOrDefaultAsync(m => m.Id == modelId)
                        ?? throw NotFound(modelId);
            RequireWritable(db, model);
            RefuseRetired(model);

            var entityIds = wanted.Select(w => w.EntityId).Distinct().ToList();
            var definitions = await db.Entities.AsNoTracking()
                .Where(e => entityIds.Contains(e.Id))
                .Select(e => new { e.Id, e.DefinitionName })
                .ToDictionaryAsync(e => e.Id, e => e.DefinitionName);

            foreach (var entityId in entityIds)
            {
                if (!definitions.TryGetValue(entityId, out var definition))
                    throw new DataNotFoundException("entities", entityId.ToString(Invariant));

                // The record the Stage 9.11 catalogue is keyed by (S53 D4): a group is a folder, and anything else is not data.
                if (definition != RiskChainSchema.DataDefinition)
                    throw new RuleBrokenException(
                        $"A model's data is a data record (organizationData), not a '{definition}'.", DataTargetRule);
            }

            var now = Clock();

            // A declaration that stays keeps its id and its trail; one that goes is removed; a new one is added.
            foreach (var stale in model.DataLinks.Where(l => !wanted.Any(w => w.EntityId == l.EntityId && w.Usage == l.Usage))
                         .ToList())
                db.AiModelDataLinks.Remove(stale);

            foreach (var item in wanted.Where(w => !model.DataLinks.Any(l => l.EntityId == w.EntityId && l.Usage == w.Usage)))
                db.AiModelDataLinks.Add(new AiModelDataLink
                {
                    ModelId = modelId, EntityId = item.EntityId, Usage = item.Usage, CreatedAt = now,
                    CreatedById = actingUserId
                });

            model.DataDeclaredAt = now;
            model.UpdatedAt = now;
            model.UpdatedById = actingUserId;

            await db.SaveChangesAsync();
        }

        Logger.Information("AI model {Id}: {Count} data record use(s) declared by user {User}", modelId, wanted.Count,
            actingUserId);
        return await GetModelAsync(modelId);
    }

    // --- writes: metrics and overrides (T214, T215) ------------------------------------------------------------------

    public async Task<AiModelReadingDto> RecordReadingAsync(int modelId, AiModelReadingRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = Clock();
        var valid = ValidateReading(request, now);

        AiModelMetricReading reading;
        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var model = await db.AiModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modelId) ?? throw NotFound(modelId);
            RequireWritable(db, model);
            RefuseRetired(model);
            RequireOfCurrentVersion(model, valid.PeriodStart, nameof(AiModelReadingRequest.PeriodStart));
            RequireOfCurrentVersion(model, valid.MeasuredAt, nameof(AiModelReadingRequest.MeasuredAt));

            var value = valid.Value;
            int? overrideCount = null;

            if (valid.Metric == AiModelMetric.HumanOverrideRate)
            {
                // Computed, never typed (S53 D8): the live overrides of the current version recorded in the period, each with
                // its author and reason, over the outputs people reviewed in it.
                var start = valid.PeriodStart!.Value;
                var end = valid.PeriodEnd!.Value;
                var count = await db.AiModelOverrides.CountAsync(o =>
                    o.ModelId == modelId && o.VoidedAt == null && o.ModelVersion == model.Version &&
                    o.OccurredAt >= start && o.OccurredAt < end);

                if (count > valid.SampleSize!.Value)
                    throw new InvalidParameterException(nameof(AiModelReadingRequest.SampleSize),
                        $"{count} override(s) are recorded in the period, more than the {valid.SampleSize} output(s) declared " +
                        "reviewed in it.");

                overrideCount = count;
                value = decimal.Round((decimal)count / valid.SampleSize.Value, AiGovernanceLimits.MetricValueScale);
            }

            reading = new AiModelMetricReading
            {
                ModelId = modelId, Metric = valid.Metric!.Value, Value = value!.Value, ModelVersion = model.Version,
                MeasuredAt = valid.MeasuredAt!.Value, PeriodStart = valid.PeriodStart, PeriodEnd = valid.PeriodEnd,
                SampleSize = valid.SampleSize, OverrideCount = overrideCount, Method = valid.Method,
                EvidenceReference = valid.EvidenceReference, RecordedById = actingUserId, CreatedAt = now
            };

            db.AiModelMetricReadings.Add(reading);
            await db.SaveChangesAsync();
        }

        Logger.Information("AI model {Id}: {Metric} reading {Reading} recorded by user {User}", modelId, reading.Metric,
            reading.Id, actingUserId);
        return ToDto(reading);
    }

    public async Task<AiModelReadingDto> VoidReadingAsync(int modelId, int readingId, AiGovernanceReasonRequest request,
        int actingUserId)
    {
        var reason = Reason(request?.Reason, AiGovernanceLimits.MaxVoidOrRetireReasonLength,
            "Voiding a reading needs a written reason: the reading stays in the history, and why it no longer counts is part of it.");

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireWritableModelAsync(db, modelId);

        var reading = await db.AiModelMetricReadings.FirstOrDefaultAsync(r => r.Id == readingId && r.ModelId == modelId)
                      ?? throw new DataNotFoundException("ai_model_metric_readings", readingId.ToString(Invariant));

        if (reading.VoidedAt is not null)
            throw new RuleBrokenException("This reading is already voided.", ReadingVoidedRule);

        reading.VoidedAt = Clock();
        reading.VoidedById = actingUserId;
        reading.VoidReason = reason;
        await db.SaveChangesAsync();

        Logger.Warning("AI model {Id}: reading {Reading} voided by user {User}", modelId, readingId, actingUserId);
        return ToDto(reading);
    }

    public async Task<AiModelOverrideDto> RecordOverrideAsync(int modelId, AiModelOverrideRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = Clock();

        var occurredAt = Utc(request.OccurredAt)
                         ?? throw new InvalidParameterException(nameof(AiModelOverrideRequest.OccurredAt),
                             "When the person overrode the model is required.");
        if (occurredAt > now)
            throw new InvalidParameterException(nameof(AiModelOverrideRequest.OccurredAt), "An override cannot be in the future.");

        var output = Text(request.ModelOutput, AiGovernanceLimits.MaxModelOutputLength,
            nameof(AiModelOverrideRequest.ModelOutput), "What the model proposed is required.");
        var decision = Text(request.HumanDecision, AiGovernanceLimits.MaxHumanDecisionLength,
            nameof(AiModelOverrideRequest.HumanDecision), "What the person decided instead is required.");
        var reason = Reason(request.Reason, AiGovernanceLimits.MaxOverrideReasonLength,
            "An override needs its reason: an override nobody can explain afterwards is not one.");

        AiModelOverride record;
        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var model = await RequireWritableModelAsync(db, modelId);
            RequireOfCurrentVersion(model, occurredAt, nameof(AiModelOverrideRequest.OccurredAt));

            record = new AiModelOverride
            {
                ModelId = modelId, ModelVersion = model.Version, OccurredAt = occurredAt, ModelOutput = output,
                HumanDecision = decision, Reason = reason, RecordedById = actingUserId, CreatedAt = now
            };

            db.AiModelOverrides.Add(record);
            await db.SaveChangesAsync();
        }

        Logger.Information("AI model {Id}: override {Override} recorded by user {User}", modelId, record.Id, actingUserId);
        return ToDto(record);
    }

    public async Task<AiModelOverrideDto> VoidOverrideAsync(int modelId, int overrideId, AiGovernanceReasonRequest request,
        int actingUserId)
    {
        var reason = Reason(request?.Reason, AiGovernanceLimits.MaxVoidOrRetireReasonLength,
            "Voiding an override needs a written reason: it stays in the history, and why it no longer counts is part of it.");

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireWritableModelAsync(db, modelId);

        var record = await db.AiModelOverrides.FirstOrDefaultAsync(o => o.Id == overrideId && o.ModelId == modelId)
                     ?? throw new DataNotFoundException("ai_model_overrides", overrideId.ToString(Invariant));

        if (record.VoidedAt is not null)
            throw new RuleBrokenException("This override is already voided.", OverrideVoidedRule);

        record.VoidedAt = Clock();
        record.VoidedById = actingUserId;
        record.VoidReason = reason;
        await db.SaveChangesAsync();

        Logger.Warning("AI model {Id}: override {Override} voided by user {User}", modelId, overrideId, actingUserId);
        return ToDto(record);
    }

    // --- writes: the register's risks (T213) --------------------------------------------------------------------------

    public async Task<RiskAiModelsDto> LinkRiskAsync(int modelId, int riskId, AiModelRiskLinkRequest request,
        int actingUserId)
    {
        var note = Text(request?.Note, AiGovernanceLimits.MaxRiskLinkNoteLength, nameof(AiModelRiskLinkRequest.Note), null);

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        // Linking is editing the risk (as the chain and the legal requirements are): the risk must be visible, and the
        // model visible too — a caller cannot reach, by id, a model outside their scope.
        await RequireVisibleRiskAsync(db, riskId);
        var model = await db.AiModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modelId) ?? throw NotFound(modelId);
        RefuseRetired(model);

        var link = await db.AiModelRisks.FirstOrDefaultAsync(l => l.ModelId == modelId && l.RiskId == riskId);
        if (link is null)
        {
            db.AiModelRisks.Add(new AiModelRisk
            {
                ModelId = modelId, RiskId = riskId, Note = note, CreatedAt = Clock(), CreatedById = actingUserId
            });
        }
        else
        {
            // Idempotent: linking again only restates the note.
            link.Note = note;
        }

        await db.SaveChangesAsync();

        Logger.Information("Risk {Risk} linked to AI model {Id} by user {User}", riskId, modelId, actingUserId);
        return await ComposeRiskViewAsync(db, riskId);
    }

    public async Task UnlinkRiskAsync(int modelId, int riskId, int actingUserId)
    {
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireVisibleRiskAsync(db, riskId);

        var link = await db.AiModelRisks.FirstOrDefaultAsync(l => l.ModelId == modelId && l.RiskId == riskId)
                   ?? throw new DataNotFoundException("ai_model_risks", $"{modelId}/{riskId}");

        db.AiModelRisks.Remove(link);
        await db.SaveChangesAsync();

        Logger.Information("Risk {Risk} unlinked from AI model {Id} by user {User}", riskId, modelId, actingUserId);
    }

    // --- composition -------------------------------------------------------------------------------------------------

    private async Task<List<AiModelDto>> ComposeAsync(AuditableContext db, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return [];

        var now = Clock();
        var idList = ids.ToList();

        var models = await db.AiModels.AsNoTracking().Where(m => idList.Contains(m.Id)).ToListAsync();

        var dataLinks = await db.AiModelDataLinks.AsNoTracking().Where(l => idList.Contains(l.ModelId)).ToListAsync();
        var dataNodes = dataLinks.Select(l => l.EntityId).Distinct().ToList();
        var names = await LoadNamesAsync(db, dataNodes);
        // The Stage 9.11 catalogue, read through the node it is keyed by — no column on any link (S53 D4).
        var catalogue = (await db.DataCatalogueEntries.AsNoTracking()
                .Where(e => dataNodes.Contains(e.EntityId))
                .Select(e => new { e.EntityId, e.PersonalData })
                .ToListAsync())
            .ToDictionary(e => e.EntityId, e => e.PersonalData);

        var readings = (await db.AiModelMetricReadings.AsNoTracking()
                .Where(r => idList.Contains(r.ModelId))
                .Select(r => new { r.ModelId, Facts = new AiReadingFacts(r.Id, r.Metric, r.Value, r.ModelVersion, r.MeasuredAt, r.VoidedAt != null) })
                .ToListAsync())
            .GroupBy(r => r.ModelId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Facts).ToList());

        var overrides = (await db.AiModelOverrides.AsNoTracking()
                .Where(o => idList.Contains(o.ModelId) && o.VoidedAt == null)
                .Select(o => new { o.ModelId, o.ModelVersion })
                .ToListAsync())
            .GroupBy(o => o.ModelId)
            .ToDictionary(g => g.Key, g => g.Select(o => o.ModelVersion).ToList());

        // The links the reader sees (the filter needs the risk and the model visible) and, unscoped, how many there are in
        // all: a finding about the register must not depend on who asks, and a hidden risk is counted, never named.
        var visibleLinks = await db.AiModelRisks.AsNoTracking()
            .Where(l => idList.Contains(l.ModelId))
            .Join(db.Risks.AsNoTracking(), l => l.RiskId, r => r.Id, (l, r) => new
            {
                l.ModelId,
                Dto = new AiModelRiskDto
                {
                    RiskId = r.Id, ReferenceId = r.ReferenceId, Subject = r.Subject, Status = r.Status, Note = l.Note,
                    CreatedAt = l.CreatedAt, CreatedById = l.CreatedById
                }
            })
            .ToListAsync();

        Dictionary<int, int> totalLinks;
        await using (var system = SystemContext())
            totalLinks = (await system.AiModelRisks.AsNoTracking()
                    .Where(l => idList.Contains(l.ModelId))
                    .GroupBy(l => l.ModelId)
                    .Select(g => new { ModelId = g.Key, Count = g.Count() })
                    .ToListAsync())
                .ToDictionary(x => x.ModelId, x => x.Count);

        var vendorIds = models.Where(m => m.ThirdPartyId != null).Select(m => m.ThirdPartyId!.Value).Distinct().ToList();
        var vendors = await db.ThirdParties.AsNoTracking()
            .Where(t => vendorIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        return models.Select(model =>
        {
            var evaluation = AiModelEvaluator.Evaluate(model.Version, model.VersionSince, model.RiskTier,
                model.HumanOversight, model.MaxEvaluationAgeDays, readings.GetValueOrDefault(model.Id) ?? [], now);

            var data = dataLinks.Where(l => l.ModelId == model.Id)
                .Select(l => new AiModelDataLinkDto
                {
                    Id = l.Id, EntityId = l.EntityId, Name = names.GetValueOrDefault(l.EntityId), Usage = l.Usage,
                    Catalogued = catalogue.ContainsKey(l.EntityId), PersonalData = catalogue.GetValueOrDefault(l.EntityId)
                })
                .OrderBy(d => d.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.EntityId)
                .ThenBy(d => d.Usage)
                .ToList();

            var risks = visibleLinks.Where(l => l.ModelId == model.Id).Select(l => l.Dto)
                .OrderBy(r => r.RiskId).ToList();
            var total = totalLinks.GetValueOrDefault(model.Id);

            var findings = AiModelFindingsEvaluator.Evaluate(new AiModelFacts(model.Status, model.Source, model.ThirdPartyId,
                model.OwnerId, model.RiskTier, model.HumanOversight, model.DataDeclaredAt,
                data.Where(d => !d.Catalogued).Select(d => d.EntityId).Distinct().Count(), total, evaluation));

            var vendorVisible = model.ThirdPartyId is { } vendor && vendors.ContainsKey(vendor);

            return new AiModelDto
            {
                Id = model.Id, Name = model.Name, Purpose = model.Purpose, Kind = model.Kind, Source = model.Source,
                ThirdPartyId = model.ThirdPartyId,
                ThirdPartyName = vendorVisible ? vendors[model.ThirdPartyId!.Value] : null,
                ThirdPartyHidden = model.ThirdPartyId is not null && !vendorVisible,
                Version = model.Version, VersionSince = model.VersionSince, Status = model.Status, RiskTier = model.RiskTier,
                HumanOversight = model.HumanOversight, OwnerId = model.OwnerId, EntityId = model.EntityId,
                MaxEvaluationAgeDays = model.MaxEvaluationAgeDays, DataDeclaredAt = model.DataDeclaredAt,
                Notes = model.Notes, RetiredAt = model.RetiredAt, RetiredById = model.RetiredById,
                RetireReason = model.RetireReason,
                Data = data,
                Evaluation = evaluation,
                Risks = risks,
                HiddenRiskCount = Math.Max(0, total - risks.Count),
                OverrideCount = (overrides.GetValueOrDefault(model.Id) ?? [])
                    .Count(v => string.Equals(v, model.Version, StringComparison.Ordinal)),
                Findings = findings,
                CreatedAt = model.CreatedAt, CreatedById = model.CreatedById, UpdatedAt = model.UpdatedAt,
                UpdatedById = model.UpdatedById
            };
        }).ToList();
    }

    private async Task<RiskAiModelsDto> ComposeRiskViewAsync(AuditableContext db, int riskId)
    {
        var links = await db.AiModelRisks.AsNoTracking()
            .Where(l => l.RiskId == riskId)
            .Select(l => new { l.ModelId, l.Note })
            .ToListAsync();

        int total;
        await using (var system = SystemContext())
            total = await system.AiModelRisks.CountAsync(l => l.RiskId == riskId);

        var models = await ComposeAsync(db, links.Select(l => l.ModelId).ToList());

        return new RiskAiModelsDto
        {
            RiskId = riskId,
            Models = models
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Id)
                .Select(m => new RiskAiModelDto
                {
                    ModelId = m.Id, Name = m.Name, Version = m.Version, Status = m.Status, RiskTier = m.RiskTier,
                    EvaluationState = m.Evaluation.State, Note = links.First(l => l.ModelId == m.Id).Note
                })
                .ToList(),
            HiddenModelCount = Math.Max(0, total - links.Count)
        };
    }

    private static AiModelReadingDto ToDto(AiModelMetricReading r) => new()
    {
        Id = r.Id, ModelId = r.ModelId, Metric = r.Metric, Value = r.Value, ModelVersion = r.ModelVersion,
        MeasuredAt = r.MeasuredAt, PeriodStart = r.PeriodStart, PeriodEnd = r.PeriodEnd, SampleSize = r.SampleSize,
        OverrideCount = r.OverrideCount, Method = r.Method, EvidenceReference = r.EvidenceReference,
        RecordedById = r.RecordedById, CreatedAt = r.CreatedAt, VoidedAt = r.VoidedAt, VoidedById = r.VoidedById,
        VoidReason = r.VoidReason
    };

    private static AiModelOverrideDto ToDto(AiModelOverride o) => new()
    {
        Id = o.Id, ModelId = o.ModelId, ModelVersion = o.ModelVersion, OccurredAt = o.OccurredAt,
        ModelOutput = o.ModelOutput, HumanDecision = o.HumanDecision, Reason = o.Reason, RecordedById = o.RecordedById,
        CreatedAt = o.CreatedAt, VoidedAt = o.VoidedAt, VoidedById = o.VoidedById, VoidReason = o.VoidReason
    };

    // --- validation --------------------------------------------------------------------------------------------------

    /// <summary>Validates and normalizes a request; the copy returned carries the trimmed values.</summary>
    private static AiModelRequest Validate(AiModelRequest request, DateTime now)
    {
        var valid = new AiModelRequest
        {
            Name = Text(request.Name, AiGovernanceLimits.MaxNameLength, nameof(AiModelRequest.Name), "The model needs a name."),
            Purpose = Text(request.Purpose, AiGovernanceLimits.MaxPurposeLength, nameof(AiModelRequest.Purpose),
                "The model's purpose is required — what it is used for is the first thing the inventory records."),
            Kind = request.Kind,
            Source = request.Source,
            ThirdPartyId = request.ThirdPartyId,
            Version = Text(request.Version, AiGovernanceLimits.MaxVersionLength, nameof(AiModelRequest.Version),
                "The version in use is required: an evaluation is of a version."),
            VersionSince = Utc(request.VersionSince),
            Status = request.Status,
            RiskTier = request.RiskTier,
            HumanOversight = request.HumanOversight,
            OwnerId = request.OwnerId,
            EntityId = request.EntityId,
            MaxEvaluationAgeDays = request.MaxEvaluationAgeDays,
            Notes = Text(request.Notes, AiGovernanceLimits.MaxNotesLength, nameof(AiModelRequest.Notes), null)
        };

        if (valid.Kind is not { } kind || !Enum.IsDefined(kind))
            throw new InvalidParameterException(nameof(AiModelRequest.Kind),
                "The kind is classification (1), forecasting (2), generative (3), recommendation (4), anomaly detection (5), " +
                "biometric (6) or other (7).");

        if (valid.Source is not { } source || !Enum.IsDefined(source))
            throw new InvalidParameterException(nameof(AiModelRequest.Source),
                "The source is in-house (1), a vendor (2) or open source (3).");

        if (valid.ThirdPartyId is <= 0)
            throw new InvalidParameterException(nameof(AiModelRequest.ThirdPartyId), "The vendor is a third party id.");
        if (valid.ThirdPartyId is not null && source != AiModelSource.Vendor)
            throw new InvalidParameterException(nameof(AiModelRequest.ThirdPartyId), "Only a vendor model names a vendor.");

        if (valid.Status is { } status && (!Enum.IsDefined(status) || status == AiModelStatus.Retired))
            throw new InvalidParameterException(nameof(AiModelRequest.Status),
                "The status is proposed (1), pilot (2) or production (3); a model is retired through its own route, with a reason.");

        if (valid.RiskTier is { } tier && !Enum.IsDefined(tier))
            throw new InvalidParameterException(nameof(AiModelRequest.RiskTier), "The risk tier is minimal (1), limited (2) or high (3).");

        if (valid.HumanOversight is { } oversight && !Enum.IsDefined(oversight))
            throw new InvalidParameterException(nameof(AiModelRequest.HumanOversight),
                "The oversight is every output reviewed (1), a sample reviewed (2) or no review (3).");

        if (valid.OwnerId is <= 0)
            throw new InvalidParameterException(nameof(AiModelRequest.OwnerId), "The owner is a user id.");
        if (valid.EntityId is <= 0)
            throw new InvalidParameterException(nameof(AiModelRequest.EntityId), "The entity is an entity id.");

        if (valid.VersionSince > now)
            throw new InvalidParameterException(nameof(AiModelRequest.VersionSince),
                "A version cannot be in use since a date in the future.");

        if (valid.MaxEvaluationAgeDays is not (>= 1 and <= AiGovernanceLimits.MaxEvaluationAgeDays))
            throw new InvalidParameterException(nameof(AiModelRequest.MaxEvaluationAgeDays),
                $"How long an evaluation stays current is required: 1 to {AiGovernanceLimits.MaxEvaluationAgeDays} days.");

        return valid;
    }

    private static List<(int EntityId, AiModelDataUsage Usage)> ValidateData(List<AiModelDataLinkRequest>? items)
    {
        if (items is null)
            throw new InvalidParameterException(nameof(AiModelDataRequest.Data),
                "The list is required — send an empty list to declare that the model uses no catalogued data.");
        if (items.Count > AiGovernanceLimits.MaxDataLinks)
            throw new InvalidParameterException(nameof(AiModelDataRequest.Data),
                $"At most {AiGovernanceLimits.MaxDataLinks} data record uses.");

        var wanted = new List<(int, AiModelDataUsage)>();
        foreach (var item in items)
        {
            if (item?.EntityId is not ({ } entityId and > 0))
                throw new InvalidParameterException(nameof(AiModelDataLinkRequest.EntityId), "Each use names a data record id.");
            if (item.Usage is not { } usage || !Enum.IsDefined(usage))
                throw new InvalidParameterException(nameof(AiModelDataLinkRequest.Usage),
                    "The use is training (1), fine-tuning (2), evaluation (3), input (4) or output (5).");
            if (wanted.Contains((entityId, usage)))
                throw new InvalidParameterException(nameof(AiModelDataRequest.Data),
                    $"Data record {entityId} is declared twice for the same use.");

            wanted.Add((entityId, usage));
        }

        return wanted;
    }

    private static AiModelReadingRequest ValidateReading(AiModelReadingRequest request, DateTime now)
    {
        if (request.Metric is not { } metric || !Enum.IsDefined(metric))
            throw new InvalidParameterException(nameof(AiModelReadingRequest.Metric),
                "The metric is accuracy (1), precision (2), recall (3), calibration (4), drift (5) or the human override rate (6).");

        var valid = new AiModelReadingRequest
        {
            Metric = metric,
            Value = request.Value,
            MeasuredAt = Utc(request.MeasuredAt),
            PeriodStart = Utc(request.PeriodStart),
            PeriodEnd = Utc(request.PeriodEnd),
            SampleSize = request.SampleSize,
            Method = Text(request.Method, AiGovernanceLimits.MaxMethodLength, nameof(AiModelReadingRequest.Method), null),
            EvidenceReference = Text(request.EvidenceReference, AiGovernanceLimits.MaxEvidenceReferenceLength,
                nameof(AiModelReadingRequest.EvidenceReference), null)
        };

        if (valid.SampleSize is not null and not (>= 1 and <= AiGovernanceLimits.MaxSampleSize))
            throw new InvalidParameterException(nameof(AiModelReadingRequest.SampleSize),
                $"The sample size is 1 to {AiGovernanceLimits.MaxSampleSize:N0}.");

        if ((valid.PeriodStart is null) != (valid.PeriodEnd is null))
            throw new InvalidParameterException(
                valid.PeriodStart is null ? nameof(AiModelReadingRequest.PeriodStart) : nameof(AiModelReadingRequest.PeriodEnd),
                "A period has a start and an end.");
        if (valid.PeriodStart is { } start && valid.PeriodEnd is { } end)
        {
            if (end <= start)
                throw new InvalidParameterException(nameof(AiModelReadingRequest.PeriodEnd), "The period ends after it starts.");
            if (end > now)
                throw new InvalidParameterException(nameof(AiModelReadingRequest.PeriodEnd), "The period cannot end in the future.");
        }

        if (metric == AiModelMetric.HumanOverrideRate)
        {
            // Computed from the overrides recorded with author and reason (S53 D8): a typed rate is refused, not ignored.
            if (valid.Value is not null)
                throw new InvalidParameterException(nameof(AiModelReadingRequest.Value),
                    "The human override rate is computed from the overrides recorded in the period; do not send a value.");
            if (valid.MeasuredAt is not null)
                throw new InvalidParameterException(nameof(AiModelReadingRequest.MeasuredAt),
                    "The human override rate is measured at the end of its period; do not send a date.");
            if (valid.PeriodStart is null)
                throw new InvalidParameterException(nameof(AiModelReadingRequest.PeriodStart),
                    "The human override rate needs the period it covers.");
            if (valid.SampleSize is null)
                throw new InvalidParameterException(nameof(AiModelReadingRequest.SampleSize),
                    "The human override rate needs how many outputs people reviewed in the period.");

            valid.MeasuredAt = valid.PeriodEnd;
            return valid;
        }

        if (valid.Value is not { } value)
            throw new InvalidParameterException(nameof(AiModelReadingRequest.Value),
                "A reading needs its value — a metric that was not measured is not recorded, and reads not evaluated.");
        if (!AiModelEvaluator.IsInRange(metric, value) ||
            decimal.Round(value, AiGovernanceLimits.MetricValueScale) != value)
            throw new InvalidParameterException(nameof(AiModelReadingRequest.Value),
                metric == AiModelMetric.Drift
                    ? $"A drift statistic is 0 to {AiGovernanceLimits.MaxDriftValue}, with up to six decimals."
                    : "The value is a fraction from 0 to 1, with up to six decimals.");

        if (valid.MeasuredAt is not { } measuredAt)
            throw new InvalidParameterException(nameof(AiModelReadingRequest.MeasuredAt), "When it was measured is required.");
        if (measuredAt > now)
            throw new InvalidParameterException(nameof(AiModelReadingRequest.MeasuredAt), "A measure cannot be in the future.");

        return valid;
    }

    // --- guards ------------------------------------------------------------------------------------------------------

    private async Task RequireUniqueNameAsync(string name, int? exceptId)
    {
        // Organization-wide and unscoped: two rows for one model would split its evaluation and its risks.
        await using var system = SystemContext();
        var lowered = name.ToLowerInvariant();
        if (await system.AiModels.AnyAsync(m => m.Name.ToLower() == lowered && (exceptId == null || m.Id != exceptId)))
            throw new DataAlreadyExistsException("netrisk", "ai_models", name, $"A model named '{name}' is already registered.");
    }

    private static async Task SaveOrConflictAsync(AuditableContext db, string name)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ContinuityService.Mentions(ex, "uq_ai_models_name"))
        {
            throw new DataAlreadyExistsException("netrisk", "ai_models", name, $"A model named '{name}' is already registered.");
        }
    }

    private static async Task RequireEntityAsync(AuditableContext db, int? entityId)
    {
        if (entityId is { } entity && !await db.Entities.AnyAsync(e => e.Id == entity))
            throw new DataNotFoundException("entities", entity.ToString(Invariant));
    }

    /// <summary>
    /// The owner is a person (MIGR-TI/IA: "human roles, never AI"): an existing, enabled user who is not the third line — the
    /// third line assures the first, and does not own what it audits (S50 §4.7).
    /// </summary>
    private static async Task RequireOwnerAsync(AuditableContext db, int? ownerId)
    {
        if (ownerId is not { } id) return;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Value == id)
                   ?? throw new DataNotFoundException("user", id.ToString(Invariant));

        if (user.Enabled != true)
            throw new InvalidParameterException(nameof(AiModelRequest.OwnerId), "A disabled account cannot own an AI model.");

        await ThirdLineGuard.EnsureNotThirdLineAsync(db, id, "own an AI model");
    }

    /// <summary>The vendor is a third party the caller can see — one outside their scope is not found.</summary>
    private static async Task RequireVendorAsync(AuditableContext db, int? thirdPartyId)
    {
        if (thirdPartyId is { } id && !await db.ThirdParties.AnyAsync(t => t.Id == id))
            throw new DataNotFoundException("third_parties", id.ToString(Invariant));
    }

    /// <summary>
    /// A write on a model — on the record or on anything that hangs off it — needs the model's own entity in the caller's
    /// scope. The context's write guard sees only <see cref="AiModel"/> rows added or modified, and only where they go: a
    /// reading, an override or a data declaration would pass it (the S51 §4.10 precedent).
    /// </summary>
    private static void RequireWritable(AuditableContext db, AiModel model)
    {
        if (!db.EntityScope.Allows(model.EntityId))
            throw new DAL.Exceptions.EntityScopeViolationException(nameof(AiModel), model.EntityId, db.EntityScope.ToString());
    }

    private static async Task<AiModel> RequireWritableModelAsync(AuditableContext db, int modelId)
    {
        var model = await db.AiModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modelId) ?? throw NotFound(modelId);
        RequireWritable(db, model);
        RefuseRetired(model);
        return model;
    }

    private static async Task RequireVisibleAsync(AuditableContext db, int modelId)
    {
        if (!await db.AiModels.AsNoTracking().AnyAsync(m => m.Id == modelId)) throw NotFound(modelId);
    }

    private static async Task RequireVisibleRiskAsync(AuditableContext db, int riskId)
    {
        if (!await db.Risks.AsNoTracking().AnyAsync(r => r.Id == riskId))
            throw new DataNotFoundException("risks", riskId.ToString(Invariant));
    }

    /// <summary>
    /// A reading measured, a period started or an override that occurred before the current version was in use is not of
    /// this version (S53 D3): attributed to it, it would evaluate a version on data from before it existed.
    /// </summary>
    private static void RequireOfCurrentVersion(AiModel model, DateTime? at, string parameter)
    {
        if (at is { } when && model.VersionSince is { } since && when < since)
            throw new InvalidParameterException(parameter,
                $"Version '{model.Version}' is in use since {since:yyyy-MM-dd HH:mm} UTC; this is from before it, so it is " +
                "not of this version. Record it before changing the version, or declare when the version took effect.");
    }

    private static void RefuseRetired(AiModel model)
    {
        if (model.Status == AiModelStatus.Retired)
            throw new RuleBrokenException(
                "This model was retired: the inventory keeps it as evidence, frozen. Register the model that replaced it.",
                RetiredRule);
    }

    private static void RequireLimit(int limit)
    {
        if (limit is < 1 or > AiGovernanceLimits.MaxHistoryLimit)
            throw new InvalidParameterException(nameof(limit), $"The limit is 1 to {AiGovernanceLimits.MaxHistoryLimit}.");
    }

    private static DataNotFoundException NotFound(int modelId) => new("ai_models", modelId.ToString(Invariant));

    private static async Task<Dictionary<int, string?>> LoadNamesAsync(AuditableContext db, IReadOnlyCollection<int> ids)
    {
        var list = ids.ToList();
        if (list.Count == 0) return new Dictionary<int, string?>();

        return (await db.EntitiesProperties.AsNoTracking()
                .Where(p => list.Contains(p.Entity) && p.Type == RiskChainSchema.NameProperty)
                .Select(p => new { p.Entity, p.Value })
                .ToListAsync())
            .GroupBy(p => p.Entity)
            .ToDictionary(g => g.Key, g => (string?)g.First().Value);
    }

    /// <summary>
    /// A free text — trimmed, bounded, required when <paramref name="missing"/> says so, and refused, without echoing it, if
    /// it carries an e-mail address or a formatted CPF: the inventory describes models and cases, never a person's data
    /// (the Stage 9.11 guard, S52 D13).
    /// </summary>
    private static string Text(string? text, int max, string parameter, string? missing)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (missing is not null) throw new InvalidParameterException(parameter, missing);
            return null!;
        }

        var trimmed = text.Trim();
        if (trimmed.Length > max) throw new InvalidParameterException(parameter, $"At most {max} characters.");

        if (PersonalValueGuard.LooksLikePersonalValue(trimmed))
            throw new InvalidParameterException(parameter,
                "This text looks like it carries an e-mail address or a CPF. The inventory records models and kinds of cases, " +
                "never a person's data — describe the case instead.");

        return trimmed;
    }

    private static string Reason(string? text, int max, string missing)
    {
        var reason = Text(text, max, "Reason", missing);
        if (reason.Length < AiGovernanceLimits.MinReasonLength)
            throw new InvalidParameterException("Reason", $"The reason is at least {AiGovernanceLimits.MinReasonLength} characters.");
        return reason;
    }

    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { } v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
    };

    private static void Apply(AiModel model, AiModelRequest valid, AiModelStatus status)
    {
        model.Name = valid.Name!;
        model.Purpose = valid.Purpose!;
        model.Kind = valid.Kind!.Value;
        model.Source = valid.Source!.Value;
        model.ThirdPartyId = valid.ThirdPartyId;
        model.Version = valid.Version!;
        model.Status = status;
        model.RiskTier = valid.RiskTier;
        model.HumanOversight = valid.HumanOversight;
        model.OwnerId = valid.OwnerId;
        model.EntityId = valid.EntityId;
        model.MaxEvaluationAgeDays = valid.MaxEvaluationAgeDays!.Value;
        model.Notes = valid.Notes;
    }

    private AuditableContext SystemContext() => DalService.GetContext(withIdentity: false, bypassEntityScope: true);
}
