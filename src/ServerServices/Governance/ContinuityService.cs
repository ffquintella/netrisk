using System.Security.Claims;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Continuity;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Security;
using ServerServices.Services;
using Tools.Continuity;
using Tools.Risks;

namespace ServerServices.Governance;

/// <summary>
/// Business impact analysis and continuity (Stage 9.3, S43 §§4–6, T157–T160).
///
/// The rules live in <c>Tools.Continuity</c> as pure functions — criticality from the MTPD
/// (<see cref="ProcessCriticality"/>), test verification (<see cref="RestorationVerification"/>), the
/// cascade (<see cref="ContinuityGraph"/>), the weighted threat (<see cref="ContinuityThreatAssessor"/>)
/// and the parameters (<see cref="ContinuitySettings"/>). This class loads the organisation's
/// continuity data, applies them, and guards the writes.
///
/// <b>Writes need global scope</b>, checked through <see cref="IDalService.GetCurrentEntityScope"/>
/// before a context is even opened, so an existing entity and a missing one get the same refusal (S43
/// §6, D12). Reads cover the whole organisation: processes and services carry no scope column and the
/// entity map is not scoped. Nothing here calls IgnoreQueryFilters.
/// </summary>
public class ContinuityService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IContinuityService
{
    public const string EntityNotBiaSubjectRule = "entity_not_bia_subject";
    public const string RtoExceedsMtpdRule = "rto_exceeds_mtpd";
    public const string SelfDependencyRule = "self_dependency";
    public const string AlreadyVoidedRule = "already_voided";

    /// <summary>365 days: the largest duration any objective or measure may take.</summary>
    public const int MaxDurationMinutes = 525_600;

    public const int MaxNotesLength = 4_000;
    public const int MaxShortTextLength = 500;
    public const int MinVoidReasonLength = 10;

    private static readonly string[] SubjectDefinitions =
        [RiskChainSchema.ProcessDefinition, RiskChainSchema.ItServiceDefinition];

    private static readonly string[] SubjectProperties =
        [RiskChainSchema.NameProperty, RiskChainSchema.IsActiveProperty, RiskChainSchema.CriticalityProperty];

    private static readonly ContinuityObjective[] Objectives = [ContinuityObjective.Rto, ContinuityObjective.Rpo];

    // --- reads --------------------------------------------------------------------------------

    public async Task<List<ContinuitySubjectDto>> GetSubjectsAsync()
    {
        await using var db = DalService.GetContext();
        var world = await LoadWorldAsync(db);

        return world.Graph.Nodes.Values
            .OrderBy(n => n.DefinitionName, StringComparer.Ordinal)
            .ThenBy(n => n.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.EntityId)
            .Select(n => world.Subject(n.EntityId))
            .ToList();
    }

    public async Task<ContinuityProfileDto> GetProfileAsync(int entityId, ClaimsPrincipal? user)
    {
        await using var db = DalService.GetContext();

        await RequireSubjectAsync(db, entityId);

        var world = await LoadWorldAsync(db);
        var globalScope = DalService.GetCurrentEntityScope().IsUnrestricted;

        var dependencies = await db.BiaDependencies.AsNoTracking()
            .Where(d => d.DependentEntityId == entityId || d.ProviderEntityId == entityId)
            .OrderBy(d => d.Id)
            .ToListAsync();

        var declared = CriticalProcessCoverageCalculator.ParseCriticality(world.DeclaredCriticalityRaw(entityId));
        var bia = world.Bias.GetValueOrDefault(entityId);

        return new ContinuityProfileDto
        {
            Subject = world.Subject(entityId),
            Bia = bia is null ? null : ToDto(bia),
            DeclaredCriticality = declared,
            DeclaredCriticalityIgnored = declared is not null && bia?.MtpdMinutes is not null,
            RtoVerification = world.Verification(entityId, ContinuityObjective.Rto),
            RpoVerification = world.Verification(entityId, ContinuityObjective.Rpo),
            DependsOn = dependencies.Where(d => d.DependentEntityId == entityId).Select(d => ToDto(d, world)).ToList(),
            DependedOnBy = dependencies.Where(d => d.ProviderEntityId == entityId).Select(d => ToDto(d, world)).ToList(),
            Cascade = world.Cascade(entityId),
            Threat = world.Threat(entityId),
            CallerCanManageBia = globalScope && ContinuityAccess.CanManageBia(user),
            CallerCanRecordTests = globalScope && ContinuityAccess.CanRecordTests(user)
        };
    }

    public async Task<List<RestorationTestDto>> GetRestorationTestsAsync(int entityId)
    {
        await using var db = DalService.GetContext();

        await RequireSubjectAsync(db, entityId);

        var tests = await db.RestorationTests.AsNoTracking()
            .Where(t => t.EntityId == entityId)
            .OrderByDescending(t => t.TestedAt)
            .ThenByDescending(t => t.Id)
            .ToListAsync();

        return tests.Select(ToDto).ToList();
    }

    public async Task<RestorationVerificationMetricDto> GetRestorationVerificationMetricAsync()
    {
        await using var db = DalService.GetContext();
        var world = await LoadWorldAsync(db);

        var active = world.Graph.Nodes.Values.Where(n => n.IsActive).OrderBy(n => n.EntityId).ToList();
        var critical = active.Where(n => n.IsCriticalProcess).ToList();

        var metric = new RestorationVerificationMetricDto
        {
            ComputedAt = world.NowUtc,
            ValidityDays = world.Settings.RestorationTestValidityDays,
            UnverifiedWeight = world.Settings.UnverifiedThreatWeight,
            All = world.Summarise(active),
            CriticalProcesses = world.Summarise(critical),
            Rows = active.Select(n => world.Subject(n.EntityId)).ToList()
        };

        foreach (var process in critical)
        {
            var threat = world.Threat(process.EntityId);
            metric.ThreatenedCriticalProcessesWeighted += threat.ThreatWeight;

            if (!threat.IsThreatened) continue;

            if (threat.Items.Any(i => i.Class == ContinuityThreatClass.Confirmed))
                metric.ThreatenedCriticalProcessesConfirmed++;
            else
                metric.ThreatenedCriticalProcessesUnverifiedOnly++;
        }

        return metric;
    }

    public async Task<List<CriticalProcessThreatDto>> GetCriticalProcessThreatsAsync()
    {
        await using var db = DalService.GetContext();
        var world = await LoadWorldAsync(db);

        var result = new List<CriticalProcessThreatDto>();

        foreach (var process in world.Graph.Nodes.Values
                     .Where(n => n.IsActive && n.IsCriticalProcess)
                     .OrderBy(n => n.EntityId))
        {
            var threat = world.Threat(process.EntityId);
            if (!threat.IsThreatened) continue;

            result.Add(new CriticalProcessThreatDto
            {
                EntityId = process.EntityId,
                Name = process.Name,
                ThreatWeight = threat.ThreatWeight,
                Confirmed = threat.Items.Any(i => i.Class == ContinuityThreatClass.Confirmed),
                ProviderEntityIds = world.Graph.Providers(process.EntityId).Keys.OrderBy(id => id).ToList()
            });
        }

        return result;
    }

    public async Task<ContinuityGraph> GetGraphAsync()
    {
        await using var db = DalService.GetContext();
        return (await LoadWorldAsync(db)).Graph;
    }

    public async Task<ContinuitySettingsDto> GetSettingsAsync()
    {
        await using var db = DalService.GetContext();
        return await ReadSettingsAsync(db);
    }

    // --- writes: BIA --------------------------------------------------------------------------

    public async Task<BusinessImpactAnalysisWriteResult> SaveBiaAsync(int entityId,
        BusinessImpactAnalysisRequest request, int? actingUserId)
    {
        RequireGlobalScope(actingUserId, "Continuity.SaveBia");

        if (request is null)
            throw new InvalidParameterException("request", "A business impact analysis is required.");

        ValidateDuration(nameof(request.MtpdMinutes), request.MtpdMinutes);
        ValidateDuration(nameof(request.RtoMinutes), request.RtoMinutes);
        ValidateDuration(nameof(request.RpoMinutes), request.RpoMinutes);

        if (request.MtpdMinutes is null && request.RtoMinutes is null && request.RpoMinutes is null)
            throw new InvalidParameterException("Bia",
                "Declare at least one of MTPD/MAO, RTO and RPO. To remove the analysis, delete it.");

        var now = DateTime.UtcNow;
        DateTime? assessedAt = request.AssessedAt is { } a ? ToUtc(a) : null;
        if (assessedAt > now)
            throw new InvalidParameterException(nameof(request.AssessedAt), "The analysis date cannot be in the future.");

        var notes = Normalise(request.Notes);
        if (notes?.Length > MaxNotesLength)
            throw new InvalidParameterException(nameof(request.Notes), $"Notes are limited to {MaxNotesLength} characters.");

        await using var db = DalService.GetContext();

        await RequireSubjectAsync(db, entityId);

        if (request.RtoMinutes is { } rto && request.MtpdMinutes is { } mtpd && rto > mtpd)
            throw new RuleBrokenException(
                "The RTO cannot exceed the MTPD/MAO: recovering after the tolerable disruption has passed is not recovering.",
                RtoExceedsMtpdRule);

        var existing = await db.BusinessImpactAnalyses.FirstOrDefaultAsync(b => b.EntityId == entityId);

        if (existing is null)
        {
            var created = new BusinessImpactAnalysis
            {
                EntityId = entityId,
                MtpdMinutes = request.MtpdMinutes,
                RtoMinutes = request.RtoMinutes,
                RpoMinutes = request.RpoMinutes,
                AssessedAt = assessedAt ?? now,
                Notes = notes,
                CreatedAt = now,
                CreatedById = actingUserId is > 0 ? actingUserId : null
            };

            db.BusinessImpactAnalyses.Add(created);
            await SaveOrConflictAsync(db, "business_impact_analyses", "uq_business_impact_analyses_entity_id",
                entityId.ToString(), "This node already has a business impact analysis. Reload and edit it.");

            return new BusinessImpactAnalysisWriteResult { Created = true, Bia = ToDto(created) };
        }

        var newAssessedAt = assessedAt ?? existing.AssessedAt;
        var changed = existing.MtpdMinutes != request.MtpdMinutes
                      || existing.RtoMinutes != request.RtoMinutes
                      || existing.RpoMinutes != request.RpoMinutes
                      || existing.AssessedAt != newAssessedAt
                      || !string.Equals(existing.Notes, notes, StringComparison.Ordinal);

        if (changed)
        {
            existing.MtpdMinutes = request.MtpdMinutes;
            existing.RtoMinutes = request.RtoMinutes;
            existing.RpoMinutes = request.RpoMinutes;
            existing.AssessedAt = newAssessedAt;
            existing.Notes = notes;
            existing.UpdatedAt = now;
            existing.UpdatedById = actingUserId is > 0 ? actingUserId : null;

            await db.SaveChangesAsync();
        }

        return new BusinessImpactAnalysisWriteResult { Created = false, Bia = ToDto(existing) };
    }

    public async Task DeleteBiaAsync(int entityId)
    {
        RequireGlobalScope(null, "Continuity.DeleteBia");

        await using var db = DalService.GetContext();

        await RequireSubjectAsync(db, entityId);

        var bia = await db.BusinessImpactAnalyses.FirstOrDefaultAsync(b => b.EntityId == entityId)
                  ?? throw new DataNotFoundException("BusinessImpactAnalysis", entityId.ToString());

        db.BusinessImpactAnalyses.Remove(bia);
        await db.SaveChangesAsync();
    }

    // --- writes: dependencies -----------------------------------------------------------------

    public async Task<BiaDependencyDto> AddDependencyAsync(int entityId, BiaDependencyCreateRequest request,
        int? actingUserId)
    {
        RequireGlobalScope(actingUserId, "Continuity.AddDependency");

        if (request is null)
            throw new InvalidParameterException("request", "A dependency is required.");

        var description = Normalise(request.Description);
        if (description?.Length > MaxShortTextLength)
            throw new InvalidParameterException(nameof(request.Description),
                $"The description is limited to {MaxShortTextLength} characters.");

        await using var db = DalService.GetContext();

        await RequireSubjectAsync(db, entityId);
        await RequireSubjectAsync(db, request.ProviderEntityId);

        if (request.ProviderEntityId == entityId)
            throw new RuleBrokenException("A process or service cannot depend on itself.", SelfDependencyRule);

        if (await db.BiaDependencies.AnyAsync(d => d.DependentEntityId == entityId
                                                    && d.ProviderEntityId == request.ProviderEntityId))
            throw AlreadyDepends(entityId, request.ProviderEntityId);

        var dependency = new BiaDependency
        {
            DependentEntityId = entityId,
            ProviderEntityId = request.ProviderEntityId,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            CreatedById = actingUserId is > 0 ? actingUserId : null
        };

        db.BiaDependencies.Add(dependency);
        await SaveOrConflictAsync(db, "bia_dependencies", "uq_bia_dependencies_dependent_entity_id_provider_entity_id",
            $"{entityId}:{request.ProviderEntityId}", "This dependency is already declared.");

        var names = await LoadNamesAsync(db, [entityId, request.ProviderEntityId]);
        return ToDto(dependency, names);
    }

    public async Task DeleteDependencyAsync(int entityId, int dependencyId)
    {
        RequireGlobalScope(null, "Continuity.DeleteDependency");

        await using var db = DalService.GetContext();

        // Another node's dependency is a plain not-found: the route names the dependent.
        var dependency = await db.BiaDependencies
                             .FirstOrDefaultAsync(d => d.Id == dependencyId && d.DependentEntityId == entityId)
                         ?? throw new DataNotFoundException("BiaDependency", dependencyId.ToString());

        db.BiaDependencies.Remove(dependency);
        await db.SaveChangesAsync();
    }

    // --- writes: restoration tests ------------------------------------------------------------

    public async Task<RestorationTestDto> RecordRestorationTestAsync(int entityId,
        RestorationTestCreateRequest request, int? actingUserId)
    {
        RequireGlobalScope(actingUserId, "Continuity.RecordRestorationTest");

        if (request is null)
            throw new InvalidParameterException("request", "A restoration test is required.");

        var now = DateTime.UtcNow;

        if (request.TestedAt == default)
            throw new InvalidParameterException(nameof(request.TestedAt), "The date of the test is required.");

        var testedAt = ToUtc(request.TestedAt);
        if (testedAt > now)
            throw new InvalidParameterException(nameof(request.TestedAt), "A test cannot be recorded before it is run.");

        if (!Enum.IsDefined(request.Outcome))
            throw new InvalidParameterException(nameof(request.Outcome), "The outcome must be Succeeded or Failed.");

        ValidateDuration(nameof(request.AchievedRtoMinutes), request.AchievedRtoMinutes);
        ValidateDuration(nameof(request.AchievedRpoMinutes), request.AchievedRpoMinutes);

        if (request.Outcome == RestorationTestOutcome.Succeeded
            && request.AchievedRtoMinutes is null && request.AchievedRpoMinutes is null)
            throw new InvalidParameterException(nameof(request.AchievedRtoMinutes),
                "A successful test measures at least one objective: the time to restore or the data window lost.");

        var evidence = Normalise(request.EvidenceReference);
        if (evidence?.Length > MaxShortTextLength)
            throw new InvalidParameterException(nameof(request.EvidenceReference),
                $"The evidence reference is limited to {MaxShortTextLength} characters.");

        var notes = Normalise(request.Notes);
        if (notes?.Length > MaxNotesLength)
            throw new InvalidParameterException(nameof(request.Notes), $"Notes are limited to {MaxNotesLength} characters.");

        await using var db = DalService.GetContext();

        await RequireSubjectAsync(db, entityId);

        var bia = await db.BusinessImpactAnalyses.AsNoTracking().FirstOrDefaultAsync(b => b.EntityId == entityId);

        var test = new RestorationTest
        {
            EntityId = entityId,
            TestedAt = testedAt,
            Outcome = request.Outcome,
            AchievedRtoMinutes = request.AchievedRtoMinutes,
            AchievedRpoMinutes = request.AchievedRpoMinutes,
            DeclaredRtoMinutes = bia?.RtoMinutes,
            DeclaredRpoMinutes = bia?.RpoMinutes,
            EvidenceReference = evidence,
            Notes = notes,
            CreatedAt = now,
            RecordedById = actingUserId is > 0 ? actingUserId : null
        };

        db.RestorationTests.Add(test);
        await db.SaveChangesAsync();

        return ToDto(test);
    }

    public async Task<RestorationTestDto> VoidRestorationTestAsync(int entityId, int testId,
        RestorationTestVoidRequest request, int? actingUserId)
    {
        RequireGlobalScope(actingUserId, "Continuity.VoidRestorationTest");

        var reason = Normalise(request?.Reason);
        if (reason is null || reason.Length < MinVoidReasonLength || reason.Length > MaxShortTextLength)
            throw new InvalidParameterException("Reason",
                $"Voiding a test needs a reason of {MinVoidReasonLength} to {MaxShortTextLength} characters.");

        await using var db = DalService.GetContext();

        var test = await db.RestorationTests.FirstOrDefaultAsync(t => t.Id == testId && t.EntityId == entityId)
                   ?? throw new DataNotFoundException("RestorationTest", testId.ToString());

        if (test.VoidedAt is not null)
            throw new RuleBrokenException("This test is already void.", AlreadyVoidedRule);

        test.VoidedAt = DateTime.UtcNow;
        test.VoidedById = actingUserId is > 0 ? actingUserId : null;
        test.VoidReason = reason;

        await db.SaveChangesAsync();

        return ToDto(test);
    }

    // --- writes: parameters -------------------------------------------------------------------

    public async Task<ContinuitySettingsDto> SaveSettingsAsync(ContinuitySettingsRequest request)
    {
        RequireGlobalScope(null, "Continuity.SaveSettings");

        if (request is null)
            throw new InvalidParameterException("request", "The two continuity parameters are required.");

        // Both refused before either is stored.
        ContinuitySettings.ValidateForWrite(request.RestorationTestValidityDays, request.UnverifiedThreatWeight);

        await using var db = DalService.GetContext();

        Upsert(db, ContinuitySettingKeys.RestorationTestValidityDays,
            ContinuitySettings.FormatValidityDays(request.RestorationTestValidityDays!.Value));
        Upsert(db, ContinuitySettingKeys.UnverifiedThreatWeight,
            ContinuitySettings.FormatWeight(request.UnverifiedThreatWeight!.Value));

        await db.SaveChangesAsync();

        return await ReadSettingsAsync(db);
    }

    // --- the loaded organisation --------------------------------------------------------------

    private async Task<World> LoadWorldAsync(AuditableContext db)
    {
        var entities = await db.Entities.AsNoTracking()
            .Where(e => SubjectDefinitions.Contains(e.DefinitionName))
            .Select(e => new { e.Id, e.DefinitionName })
            .ToListAsync();

        var properties = (await db.EntitiesProperties.AsNoTracking()
                .Where(p => SubjectProperties.Contains(p.Type)
                            && SubjectDefinitions.Contains(p.EntityNavigation.DefinitionName))
                .Select(p => new { p.Entity, p.Type, p.Value })
                .ToListAsync())
            .GroupBy(p => (p.Entity, p.Type))
            .ToDictionary(g => g.Key, g => g.First().Value);

        var bias = await db.BusinessImpactAnalyses.AsNoTracking().ToDictionaryAsync(b => b.EntityId);

        var edges = await db.BiaDependencies.AsNoTracking()
            .Select(d => new { d.DependentEntityId, d.ProviderEntityId })
            .ToListAsync();

        var tests = (await db.RestorationTests.AsNoTracking()
                .Select(t => new
                {
                    t.Id, t.EntityId, t.TestedAt, t.Outcome, t.AchievedRtoMinutes, t.AchievedRpoMinutes,
                    Voided = t.VoidedAt != null
                })
                .ToListAsync())
            .GroupBy(t => t.EntityId)
            .ToDictionary(g => g.Key, g => g.Select(t => new RestorationTestFacts(t.Id, t.TestedAt, t.Outcome,
                t.AchievedRtoMinutes, t.AchievedRpoMinutes, t.Voided)).ToList());

        var settings = await ReadSettingsAsync(db);

        string? Property(int id, string type) => properties.GetValueOrDefault((id, type));

        var nodes = entities.Select(e =>
        {
            var bia = bias.GetValueOrDefault(e.Id);
            var effective = e.DefinitionName == RiskChainSchema.ProcessDefinition
                ? ProcessCriticality.Resolve(bia?.MtpdMinutes, Property(e.Id, RiskChainSchema.CriticalityProperty)).Value
                : null;

            return new ContinuityNode(e.Id, e.DefinitionName, Property(e.Id, RiskChainSchema.NameProperty),
                IsActive(Property(e.Id, RiskChainSchema.IsActiveProperty)), bia?.MtpdMinutes, bia?.RtoMinutes,
                bia?.RpoMinutes, effective);
        });

        var graph = new ContinuityGraph(nodes, edges.Select(e => (e.DependentEntityId, e.ProviderEntityId)));

        return new World(graph, bias, tests, settings, DateTime.UtcNow,
            id => Property(id, RiskChainSchema.CriticalityProperty));
    }

    /// <summary>
    /// The organisation's continuity data loaded once per request, with the per-node results computed
    /// on first use and kept, so a list of N subjects costs one walk set per node.
    /// </summary>
    private sealed class World(
        ContinuityGraph graph,
        Dictionary<int, BusinessImpactAnalysis> bias,
        Dictionary<int, List<RestorationTestFacts>> tests,
        ContinuitySettingsDto settings,
        DateTime nowUtc,
        Func<int, string?> declaredCriticalityRaw)
    {
        private readonly Dictionary<(int, ContinuityObjective), ObjectiveVerificationDto> _verifications = new();
        private readonly Dictionary<int, ContinuityCascadeDto> _cascades = new();
        private readonly Dictionary<int, ContinuityThreatDto> _threats = new();

        public ContinuityGraph Graph { get; } = graph;
        public Dictionary<int, BusinessImpactAnalysis> Bias { get; } = bias;
        public ContinuitySettingsDto Settings { get; } = settings;
        public DateTime NowUtc { get; } = nowUtc;

        public string? DeclaredCriticalityRaw(int id) => declaredCriticalityRaw(id);

        public ObjectiveVerificationDto Verification(int id, ContinuityObjective objective)
        {
            if (_verifications.TryGetValue((id, objective), out var cached)) return cached;

            var node = Graph.Find(id);
            var result = RestorationVerification.Evaluate(objective, node?.Objective(objective),
                tests.GetValueOrDefault(id) ?? [], NowUtc, Settings.RestorationTestValidityDays);

            _verifications[(id, objective)] = result;
            return result;
        }

        public ContinuityCascadeDto Cascade(int id)
        {
            if (!_cascades.TryGetValue(id, out var cascade)) _cascades[id] = cascade = Graph.Cascade(id);
            return cascade;
        }

        public ContinuityThreatDto Threat(int id)
        {
            if (!_threats.TryGetValue(id, out var threat))
                _threats[id] = threat = ContinuityThreatAssessor.Assess(Graph, id,
                    (node, objective) => Verification(node, objective).Status, Settings.UnverifiedThreatWeight);
            return threat;
        }

        public ContinuitySubjectDto Subject(int id)
        {
            var node = Graph.Find(id)!;
            var cascade = Cascade(id);
            var source = node.DefinitionName == RiskChainSchema.ProcessDefinition
                ? ProcessCriticality.Resolve(node.MtpdMinutes, DeclaredCriticalityRaw(id)).Source
                : null;

            return new ContinuitySubjectDto
            {
                EntityId = id,
                Name = node.Name ?? string.Empty,
                DefinitionName = node.DefinitionName,
                IsActive = node.IsActive,
                HasBia = Bias.ContainsKey(id),
                MtpdMinutes = node.MtpdMinutes,
                RtoMinutes = node.RtoMinutes,
                RpoMinutes = node.RpoMinutes,
                EffectiveCriticality = node.EffectiveCriticality,
                CriticalitySource = source,
                RtoStatus = Verification(id, ContinuityObjective.Rto).Status,
                RpoStatus = Verification(id, ContinuityObjective.Rpo).Status,
                CascadeConflictCount = cascade.Conflicts.Count,
                InCycle = cascade.CycleMembers.Count > 0,
                ThreatWeight = Threat(id).ThreatWeight
            };
        }

        public RestorationVerificationSummaryDto Summarise(IReadOnlyCollection<ContinuityNode> subjects)
        {
            var summary = new RestorationVerificationSummaryDto
            {
                SubjectsWithoutBia = subjects.Count(s => !Bias.ContainsKey(s.EntityId))
            };

            foreach (var objective in Objectives)
            {
                var target = objective == ContinuityObjective.Rto ? summary.Rto : summary.Rpo;

                foreach (var subject in subjects)
                {
                    var verification = Verification(subject.EntityId, objective);
                    if (verification.Status == ObjectiveVerificationStatus.Absent) continue;

                    target.Declared++;
                    switch (verification.Status)
                    {
                        case ObjectiveVerificationStatus.Met:
                            target.Met++;
                            break;
                        case ObjectiveVerificationStatus.NotMet:
                            target.NotMet++;
                            break;
                        case ObjectiveVerificationStatus.Unverified:
                            target.Unverified++;
                            if (verification.Reason == VerificationReason.NoTest) target.UnverifiedNoTest++;
                            if (verification.Reason == VerificationReason.Stale) target.UnverifiedStale++;
                            if (verification.Reason == VerificationReason.NotMeasured) target.UnverifiedNotMeasured++;
                            break;
                    }
                }

                target.MetRatio = target.Declared == 0 ? null : (decimal)target.Met / target.Declared;
            }

            return summary;
        }
    }

    // --- helpers ------------------------------------------------------------------------------

    /// <summary>Writes need global scope; refused before any context is opened (S43 §6).</summary>
    private void RequireGlobalScope(int? actingUserId, string operation)
    {
        if (!DalService.GetCurrentEntityScope().IsUnrestricted)
            throw new PermissionInvalidException(ContinuityAccess.GlobalScope, actingUserId ?? 0, operation);
    }

    private static async Task RequireSubjectAsync(AuditableContext db, int entityId)
    {
        var definition = await db.Entities.AsNoTracking()
                             .Where(e => e.Id == entityId)
                             .Select(e => e.DefinitionName)
                             .FirstOrDefaultAsync()
                         ?? throw new DataNotFoundException("Entity", entityId.ToString());

        if (!SubjectDefinitions.Contains(definition))
            throw new RuleBrokenException(
                $"An entity of type '{definition}' has no business impact analysis: only business processes and IT services do.",
                EntityNotBiaSubjectRule);
    }

    private async Task<ContinuitySettingsDto> ReadSettingsAsync(AuditableContext db)
    {
        string[] keys = [ContinuitySettingKeys.RestorationTestValidityDays, ContinuitySettingKeys.UnverifiedThreatWeight];

        var rows = await db.Settings.AsNoTracking()
            .Where(s => keys.Contains(s.Name))
            .ToDictionaryAsync(s => s.Name, s => s.Value);

        var settings = ContinuitySettings.Parse(rows);

        if (settings.FallbackApplied.Count > 0)
            Logger.Warning("Continuity parameters {Keys} are missing or invalid; their defaults are in force",
                settings.FallbackApplied);

        return settings;
    }

    private static void Upsert(AuditableContext db, string key, string value)
    {
        var row = db.Settings.FirstOrDefault(s => s.Name == key);
        if (row is null) db.Settings.Add(new Setting { Name = key, Value = value });
        else row.Value = value;
    }

    private static void ValidateDuration(string name, int? minutes)
    {
        if (minutes is { } value && (value < 0 || value > MaxDurationMinutes))
            throw new InvalidParameterException(name,
                $"A duration is a whole number of minutes from 0 to {MaxDurationMinutes} (365 days).");
    }

    /// <summary>
    /// Saves; the loser of a race on a unique index — MariaDB 1062, recognised by the index name — is a
    /// conflict rather than a 500. The in-memory provider enforces no unique index; DAL.IntegrationTests
    /// runs the real race.
    /// </summary>
    private static async Task SaveOrConflictAsync(AuditableContext db, string table, string uniqueIndex,
        string identification, string message)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (Mentions(ex, uniqueIndex))
        {
            throw new DataAlreadyExistsException("netrisk", table, identification, message);
        }
    }

    public static bool Mentions(Exception exception, string text)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current.Message.Contains(text, StringComparison.Ordinal)) return true;

        return false;
    }

    private static DataAlreadyExistsException AlreadyDepends(int dependent, int provider) =>
        new("netrisk", "bia_dependencies", $"{dependent}:{provider}", "This dependency is already declared.");

    private static async Task<Dictionary<int, string?>> LoadNamesAsync(AuditableContext db, int[] ids) =>
        (await db.EntitiesProperties.AsNoTracking()
            .Where(p => ids.Contains(p.Entity) && p.Type == RiskChainSchema.NameProperty)
            .Select(p => new { p.Entity, p.Value })
            .ToListAsync())
        .GroupBy(p => p.Entity)
        .ToDictionary(g => g.Key, g => (string?)g.First().Value);

    private static bool IsActive(string? raw) => raw is null || !bool.TryParse(raw, out var active) || active;

    private static string? Normalise(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    private static BusinessImpactAnalysisDto ToDto(BusinessImpactAnalysis bia) => new()
    {
        Id = bia.Id,
        EntityId = bia.EntityId,
        MtpdMinutes = bia.MtpdMinutes,
        RtoMinutes = bia.RtoMinutes,
        RpoMinutes = bia.RpoMinutes,
        AssessedAt = Utc(bia.AssessedAt),
        Notes = bia.Notes,
        CreatedAt = Utc(bia.CreatedAt),
        CreatedById = bia.CreatedById,
        UpdatedAt = Utc(bia.UpdatedAt),
        UpdatedById = bia.UpdatedById
    };

    private static BiaDependencyDto ToDto(BiaDependency dependency, World world) => new()
    {
        Id = dependency.Id,
        DependentEntityId = dependency.DependentEntityId,
        DependentName = world.Graph.Find(dependency.DependentEntityId)?.Name,
        ProviderEntityId = dependency.ProviderEntityId,
        ProviderName = world.Graph.Find(dependency.ProviderEntityId)?.Name,
        Description = dependency.Description,
        CreatedAt = Utc(dependency.CreatedAt),
        CreatedById = dependency.CreatedById
    };

    private static BiaDependencyDto ToDto(BiaDependency dependency, IReadOnlyDictionary<int, string?> names) => new()
    {
        Id = dependency.Id,
        DependentEntityId = dependency.DependentEntityId,
        DependentName = names.GetValueOrDefault(dependency.DependentEntityId),
        ProviderEntityId = dependency.ProviderEntityId,
        ProviderName = names.GetValueOrDefault(dependency.ProviderEntityId),
        Description = dependency.Description,
        CreatedAt = Utc(dependency.CreatedAt),
        CreatedById = dependency.CreatedById
    };

    private static RestorationTestDto ToDto(RestorationTest test) => new()
    {
        Id = test.Id,
        EntityId = test.EntityId,
        TestedAt = Utc(test.TestedAt),
        Outcome = test.Outcome,
        AchievedRtoMinutes = test.AchievedRtoMinutes,
        AchievedRpoMinutes = test.AchievedRpoMinutes,
        DeclaredRtoMinutes = test.DeclaredRtoMinutes,
        DeclaredRpoMinutes = test.DeclaredRpoMinutes,
        EvidenceReference = test.EvidenceReference,
        Notes = test.Notes,
        CreatedAt = Utc(test.CreatedAt),
        RecordedById = test.RecordedById,
        VoidedAt = Utc(test.VoidedAt),
        VoidedById = test.VoidedById,
        VoidReason = test.VoidReason
    };
}
