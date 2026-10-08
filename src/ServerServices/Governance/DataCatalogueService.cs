using System.Globalization;
using System.Text.RegularExpressions;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.DataCatalogue;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Security;
using ServerServices.Services;
using Tools.DataCatalogue;
using Tools.Risks;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.11 (S52) — the LGPD data catalogue: the methodology's discovery front C (data: catalogue, classification, legal
/// basis, retention, location), the RIPD (DPIA) as an artifact, and the legal and contractual requirements of the risk
/// register as links instead of free text. The rules are pure, in <c>Tools.DataCatalogue</c> — the findings
/// (<see cref="DataCatalogueFindingsEvaluator"/>), the legal bases (<see cref="LgpdLegalBases"/>) and the guard against
/// pasted personal values (<see cref="PersonalValueGuard"/>). This class loads, guards and stores.
///
/// <b>Kinds, never values.</b> The catalogue describes what a data record holds and why; it stores no data subject's value,
/// and no log line here echoes its text — only ids, counts and codes (S52 D13).
///
/// <b>Retention signals, never deletes</b> (S52 D5). An expired retention is a finding computed on read; reads write
/// nothing; there is no method here that deletes a catalogue entry — it goes with its node (CASCADE) —, and no job writes
/// or deletes the catalogue: the only job that reaches it is the nightly flag reconciliation, which reads it for flag 5
/// (<c>RiskFlagsService</c>, pinned by D9 of <c>RiskFlagsServiceInMemoryTest</c>).
///
/// <b>Scope.</b> The catalogue, the requirements and the RIPDs describe the entity map, which has no scope (S43 D12): they
/// are read by the whole audience of the read policy, and every write to them needs global scope, checked before a context
/// is opened (S52 D10). The processors of a record are named only when the reader sees them; the countries they reach are
/// computed unscoped, so a finding never depends on who asks (S52 D11). The risk links follow the risk.
/// </summary>
public class DataCatalogueService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IDataCatalogueService
{
    public const string TargetRule = "data_catalogue_target";
    public const string DpiaLinkTargetRule = "dpia_link_target";
    public const string DpiaNotDraftRule = "dpia_not_draft";
    public const string DpiaIncompleteRule = "dpia_incomplete";
    public const string DpiaRetiredRule = "dpia_retired";
    public const string RequirementInUseRule = "legal_requirement_in_use";

    /// <summary>
    /// The audited types of Stage 9.11. Their history is served by the catalogue's own history routes and by the risk's
    /// trail; the generic audit reader refuses them (S52 §4.10), as it refuses the hosts' and the third parties'.
    /// </summary>
    public static readonly IReadOnlyCollection<string> TrailTypes =
    [
        nameof(LegalRequirement), nameof(DataCatalogueEntry), nameof(DataCataloguePurpose), nameof(DataCatalogueLocation),
        nameof(Dpia), nameof(DpiaLink), nameof(RiskLegalRequirement)
    ];

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly Regex CountryCode = new("^[A-Z]{2}$", RegexOptions.CultureInvariant);

    /// <summary>The third party's id inside the "Field=value; …" summary the audit interceptor writes on a create or a delete.</summary>
    private static readonly Regex SummaryThirdPartyId = new(@"(?<![A-Za-z])ThirdPartyId=(\d+)", RegexOptions.CultureInvariant);

    /// <summary>What a scoped reader sees in the trail in place of a third party they cannot see.</summary>
    public const string HiddenValue = "(hidden)";

    /// <summary>
    /// How two purposes of one record are the same: ignoring case and accents, as the column's <c>utf8mb4_unicode_ci</c>
    /// collation compares them — "Matrícula" and "matricula" are one purpose, and must not reach the unique index as two.
    /// </summary>
    private static readonly StringComparer PurposeComparer =
        StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    /// <summary>What a RIPD may cover, and the kind each definition makes (S52 §4.4).</summary>
    private static readonly Dictionary<string, DpiaLinkKind> DpiaTargets = new(StringComparer.Ordinal)
    {
        [RiskChainSchema.DataDefinition] = DpiaLinkKind.DataRecord,
        [RiskChainSchema.ProcessDefinition] = DpiaLinkKind.BusinessProcess
    };

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    // --- data records: reads -----------------------------------------------------------------------------------------

    public async Task<List<DataRecordSummaryDto>> GetRecordsAsync(bool withFindingsOnly)
    {
        await using var db = DalService.GetContext();

        var nodes = await db.Entities.AsNoTracking()
            .Where(e => e.DefinitionName == RiskChainSchema.DataDefinition)
            .Select(e => e.Id)
            .ToListAsync();

        var world = await LoadWorldAsync(nodes);
        var now = Clock();

        return nodes
            .Select(id =>
            {
                world.Entries.TryGetValue(id, out var entry);
                return new DataRecordSummaryDto
                {
                    EntityId = id,
                    Name = world.Names.GetValueOrDefault(id),
                    Catalogued = entry is not null,
                    PersonalData = entry?.PersonalData,
                    PurposeCount = entry?.Purposes.Count ?? 0,
                    ApprovedDpiaCount = world.DpiasOf(id).Count(d => d.Status == DpiaStatus.Approved),
                    FindingCodes = DataCatalogueFindingsEvaluator.Evaluate(world.FactsOf(id), now).Select(f => f.Code).ToList()
                };
            })
            .Where(s => !withFindingsOnly || s.FindingCodes.Count > 0)
            .OrderBy(s => s.Name ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(s => s.EntityId)
            .ToList();
    }

    public async Task<DataRecordDto> GetRecordAsync(int entityId)
    {
        await using var db = DalService.GetContext();
        await RequireDataRecordAsync(db, entityId);

        var world = await LoadWorldAsync([entityId]);
        var now = Clock();
        world.Entries.TryGetValue(entityId, out var entry);

        // Named only when the reader sees them; the others are counted (S51 D8).
        var processorLinks = world.ProcessorLinks.GetValueOrDefault(entityId) ?? [];
        var processorIds = processorLinks.Select(p => p.ThirdPartyId).Distinct().ToList();
        var visible = await db.ThirdParties.AsNoTracking()
            .Where(t => processorIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Status })
            .ToDictionaryAsync(t => t.Id);

        var dto = new DataRecordDto
        {
            EntityId = entityId,
            Name = world.Names.GetValueOrDefault(entityId),
            Catalogued = entry is not null,
            Processors = processorLinks
                .Where(p => visible.ContainsKey(p.ThirdPartyId))
                .GroupBy(p => p.ThirdPartyId)
                .Select(g => new DataRecordProcessorDto
                {
                    ThirdPartyId = g.Key, Name = visible[g.Key].Name, Status = visible[g.Key].Status,
                    ThroughGroup = g.All(p => p.ThroughGroup)
                })
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.ThirdPartyId)
                .ToList(),
            HiddenProcessorCount = processorIds.Count(id => !visible.ContainsKey(id)),
            TransferCountries = DataCatalogueFindingsEvaluator.OutsideHome(
                (entry?.Locations.Select(l => l.Country) ?? []).Concat(world.ProcessorCountriesOf(entityId))),
            Dpias = world.DpiasOf(entityId)
                .OrderByDescending(d => d.Status == DpiaStatus.Approved).ThenByDescending(d => d.Id)
                .Select(d => new DataRecordDpiaDto
                {
                    Id = d.Id, Title = d.Title, Status = d.Status, ResidualRisk = d.ResidualRisk, ApprovedAt = d.ApprovedAt,
                    NextReviewDueAt = d.NextReviewDueAt
                })
                .ToList(),
            Findings = DataCatalogueFindingsEvaluator.Evaluate(world.FactsOf(entityId), now)
        };

        if (entry is null) return dto;

        dto.PersonalData = entry.PersonalData;
        dto.InvolvesMinors = entry.InvolvesMinors;
        dto.LargeVolume = entry.LargeVolume;
        dto.StrategicResearch = entry.StrategicResearch;
        dto.DataSubjects = entry.DataSubjects;
        dto.DataCategories = entry.DataCategories;
        dto.RetentionPeriodMonths = entry.RetentionPeriodMonths;
        dto.RetentionTrigger = entry.RetentionTrigger;
        dto.RetentionBasis = entry.RetentionBasis;
        dto.RetentionRequirement = Ref(entry.RetentionRequirement);
        dto.RetentionReviewDueAt = entry.RetentionReviewDueAt;
        dto.RetentionReviewedAt = entry.RetentionReviewedAt;
        dto.InternationalTransfer = entry.InternationalTransfer;
        dto.TransferMechanism = entry.TransferMechanism;
        dto.Notes = entry.Notes;
        dto.Purposes = entry.Purposes.OrderBy(p => p.Id).Select(p => new DataCataloguePurposeDto
        {
            Id = p.Id, Purpose = p.Purpose, LegalBasis = p.LegalBasis,
            LegalBasisArticle = p.LegalBasis is { } basis ? LgpdLegalBases.ArticleOf(basis) : null,
            LegalRequirement = Ref(p.LegalRequirement), BasisReference = p.BasisReference
        }).ToList();
        dto.Locations = entry.Locations.OrderBy(l => l.Country, StringComparer.Ordinal).ThenBy(l => l.Purpose)
            .Select(l => new DataCatalogueLocationDto { Id = l.Id, Country = l.Country, Region = l.Region, Purpose = l.Purpose })
            .ToList();
        dto.CreatedAt = entry.CreatedAt;
        dto.CreatedById = entry.CreatedById;
        dto.UpdatedAt = entry.UpdatedAt;
        dto.UpdatedById = entry.UpdatedById;

        return dto;
    }

    public async Task<List<AuditLog>> GetRecordHistoryAsync(int entityId, int limit)
    {
        RequireLimit(limit);

        await using var db = DalService.GetContext();
        await RequireDataRecordAsync(db, entityId);

        var entryId = await db.DataCatalogueEntries.Where(e => e.EntityId == entityId).Select(e => (int?)e.Id)
            .FirstOrDefaultAsync();
        if (entryId is not { } id) return [];

        // What it declares now: a purpose or a location since removed stays in audit_logs under its own id, which nothing
        // here can find any more — the per-risk trail has the same limit (S51 R5).
        var purposes = await db.DataCataloguePurposes.Where(p => p.EntryId == id).Select(p => p.Id).ToListAsync();
        var locations = await db.DataCatalogueLocations.Where(l => l.EntryId == id).Select(l => l.Id).ToListAsync();

        return await db.AuditLogs
            .Where(a =>
                (a.EntityType == nameof(DataCatalogueEntry) && a.EntityId == id) ||
                (a.EntityType == nameof(DataCataloguePurpose) && purposes.Contains(a.EntityId)) ||
                (a.EntityType == nameof(DataCatalogueLocation) && locations.Contains(a.EntityId)))
            .Include(a => a.User)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync();
    }

    // --- data records: the write (T207) ------------------------------------------------------------------------------

    public async Task<DataRecordDto> SaveRecordAsync(int entityId, DataCatalogueEntryRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireGlobalScope(actingUserId, "DataCatalogue.SaveRecord");
        var valid = Validate(request);
        var now = Clock();

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            await RequireDataRecordAsync(db, entityId);

            var cited = valid.Purposes!.Where(p => p.LegalRequirementId is not null).Select(p => p.LegalRequirementId!.Value)
                .Append(valid.RetentionRequirementId ?? 0).Where(id => id > 0).Distinct().ToList();
            var known = await db.LegalRequirements.Where(r => cited.Contains(r.Id)).Select(r => r.Id).ToListAsync();
            if (cited.FirstOrDefault(id => !known.Contains(id)) is var missing and > 0)
                throw new DataNotFoundException("legal_requirements", missing.ToString(Invariant));

            var entry = await db.DataCatalogueEntries
                .Include(e => e.Purposes)
                .Include(e => e.Locations)
                .FirstOrDefaultAsync(e => e.EntityId == entityId);

            if (entry is null)
            {
                entry = new DataCatalogueEntry { EntityId = entityId, CreatedAt = now, CreatedById = actingUserId };
                db.DataCatalogueEntries.Add(entry);
            }
            else
            {
                entry.UpdatedAt = now;
                entry.UpdatedById = actingUserId;
            }

            Apply(entry, valid);
            SyncPurposes(db, entry, valid.Purposes!, now, actingUserId);
            SyncLocations(db, entry, valid.Locations!, now, actingUserId);

            await db.SaveChangesAsync();
        }

        // Ids and counts only: the catalogue's text never reaches a log line (S52 D13).
        Logger.Information("Data record {EntityId} catalogued by user {User}: {Purposes} purpose(s), {Locations} location(s)",
            entityId, actingUserId, valid.Purposes!.Count, valid.Locations!.Count);

        return await GetRecordAsync(entityId);
    }

    // --- legal requirements (T209) -----------------------------------------------------------------------------------

    public async Task<List<LegalRequirementDto>> GetRequirementsAsync()
    {
        await using var db = DalService.GetContext();

        var requirements = await db.LegalRequirements.AsNoTracking()
            .OrderBy(r => r.Code).ThenBy(r => r.Id)
            .ToListAsync();

        return await ComposeRequirementsAsync(db, requirements);
    }

    public async Task<List<AuditLog>> GetRequirementHistoryAsync(int requirementId, int limit)
    {
        RequireLimit(limit);

        await using var db = DalService.GetContext();
        if (!await db.LegalRequirements.AnyAsync(r => r.Id == requirementId)) throw RequirementNotFound(requirementId);

        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(LegalRequirement) && a.EntityId == requirementId)
            .Include(a => a.User)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync();

        await MaskHiddenCounterpartiesAsync(db, rows);
        return rows;
    }

    /// <summary>
    /// A contract's counterparty is named only to whoever sees it (S52 §4.10) — in the trail too. The rows a requirement
    /// leaves carry the third party's id: in <c>OldValue</c>/<c>NewValue</c> of a <c>ThirdPartyId</c> change, and inside
    /// the "Field=value; …" summary of a creation or a deletion. For a scoped reader, every id of a third party they cannot
    /// see is replaced by <see cref="HiddenValue"/>; the rows are not tracked, so nothing is written back.
    /// </summary>
    private static async Task MaskHiddenCounterpartiesAsync(AuditableContext db, List<AuditLog> rows)
    {
        if (db.EntityScope.IsUnrestricted || rows.Count == 0) return;

        var mentioned = new HashSet<int>();
        foreach (var row in rows)
        foreach (var value in new[] { row.OldValue, row.NewValue })
        {
            if (value is null) continue;
            if (row.Field == nameof(LegalRequirement.ThirdPartyId) && int.TryParse(value, NumberStyles.Integer, Invariant, out var id))
                mentioned.Add(id);
            foreach (Match match in SummaryThirdPartyId.Matches(value))
                mentioned.Add(int.Parse(match.Groups[1].Value, Invariant));
        }

        if (mentioned.Count == 0) return;

        var ids = mentioned.ToList();
        var visible = (await db.ThirdParties.AsNoTracking().Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync())
            .ToHashSet();
        if (visible.Count == mentioned.Count) return;

        string? Mask(string? value, bool isField)
        {
            if (value is null) return null;
            if (isField)
                return int.TryParse(value, NumberStyles.Integer, Invariant, out var id) && !visible.Contains(id) ? HiddenValue : value;

            return SummaryThirdPartyId.Replace(value, m =>
                visible.Contains(int.Parse(m.Groups[1].Value, Invariant)) ? m.Value : $"ThirdPartyId={HiddenValue}");
        }

        foreach (var row in rows)
        {
            var isField = row.Field == nameof(LegalRequirement.ThirdPartyId);
            row.OldValue = Mask(row.OldValue, isField);
            row.NewValue = Mask(row.NewValue, isField);
        }
    }

    public async Task<LegalRequirementDto> CreateRequirementAsync(LegalRequirementRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireGlobalScope(actingUserId, "DataCatalogue.CreateRequirement");
        var valid = Validate(request);
        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireThirdPartyAsync(db, valid.ThirdPartyId);
        await RequireUniqueCodeAsync(db, valid.Code!, null);

        var requirement = new LegalRequirement { CreatedAt = now, CreatedById = actingUserId };
        Apply(requirement, valid);
        db.LegalRequirements.Add(requirement);
        await SaveOrConflictAsync(db, valid.Code!);

        Logger.Information("Legal requirement {Id} registered by user {User}", requirement.Id, actingUserId);
        return (await ComposeRequirementsAsync(db, [requirement])).Single();
    }

    public async Task<LegalRequirementDto> UpdateRequirementAsync(int requirementId, LegalRequirementRequest request,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireGlobalScope(actingUserId, "DataCatalogue.UpdateRequirement");
        var valid = Validate(request);

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var requirement = await db.LegalRequirements.FirstOrDefaultAsync(r => r.Id == requirementId)
                          ?? throw RequirementNotFound(requirementId);

        await RequireThirdPartyAsync(db, valid.ThirdPartyId);
        await RequireUniqueCodeAsync(db, valid.Code!, requirementId);

        Apply(requirement, valid);
        requirement.UpdatedAt = Clock();
        requirement.UpdatedById = actingUserId;
        await SaveOrConflictAsync(db, valid.Code!);

        return (await ComposeRequirementsAsync(db, [requirement])).Single();
    }

    public async Task DeleteRequirementAsync(int requirementId, int actingUserId)
    {
        RequireGlobalScope(actingUserId, "DataCatalogue.DeleteRequirement");

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var requirement = await db.LegalRequirements.FirstOrDefaultAsync(r => r.Id == requirementId)
                          ?? throw RequirementNotFound(requirementId);

        // Counted unscoped: a link from a risk the caller cannot see is a use, and must still stop the deletion.
        LegalRequirementReferences.Counts references;
        await using (var system = SystemContext())
            references = await LegalRequirementReferences.CountAsync(system, requirementId);

        if (references.Total > 0)
            throw new RuleBrokenException(
                $"Requirement '{requirement.Code}' is in use — {references.Describe()}. Unlink it first.", RequirementInUseRule);

        db.LegalRequirements.Remove(requirement);
        await db.SaveChangesAsync();

        Logger.Warning("Legal requirement {Id} DELETED by user {User}", requirementId, actingUserId);
    }

    // --- RIPD / DPIA (T208) ------------------------------------------------------------------------------------------

    public async Task<List<DpiaSummaryDto>> GetDpiasAsync(DpiaStatus? status)
    {
        if (status is { } s && !Enum.IsDefined(s))
            throw new InvalidParameterException(nameof(status), "The status is draft (1), approved (2) or retired (3).");

        await using var db = DalService.GetContext();
        var now = Clock();

        var dpias = await db.Dpias.AsNoTracking()
            .Where(d => status == null || d.Status == status)
            .Select(d => new
            {
                d.Id, d.Title, d.Status, d.ResidualRisk, d.ApprovedAt, d.NextReviewDueAt,
                DataRecords = d.Links.Count(l => l.Kind == DpiaLinkKind.DataRecord),
                Processes = d.Links.Count(l => l.Kind == DpiaLinkKind.BusinessProcess)
            })
            .ToListAsync();

        return dpias
            .OrderBy(d => d.Status).ThenByDescending(d => d.Id)
            .Select(d => new DpiaSummaryDto
            {
                Id = d.Id, Title = d.Title, Status = d.Status, ResidualRisk = d.ResidualRisk, ApprovedAt = d.ApprovedAt,
                NextReviewDueAt = d.NextReviewDueAt, ReviewOverdue = ReviewOverdue(d.Status, d.NextReviewDueAt, now),
                DataRecordCount = d.DataRecords, ProcessCount = d.Processes
            })
            .ToList();
    }

    public async Task<DpiaDto> GetDpiaAsync(int dpiaId)
    {
        await using var db = DalService.GetContext();

        var dpia = await db.Dpias.AsNoTracking()
                       .Include(d => d.Links)
                       .Include(d => d.ApprovedBy)
                       .FirstOrDefaultAsync(d => d.Id == dpiaId)
                   ?? throw DpiaNotFound(dpiaId);

        var names = await LoadNamesAsync(db, dpia.Links.Select(l => l.EntityId).ToList());
        var now = Clock();

        return new DpiaDto
        {
            Id = dpia.Id, Title = dpia.Title, Status = dpia.Status, Summary = dpia.Summary,
            DocumentReference = dpia.DocumentReference, ResidualRisk = dpia.ResidualRisk, PerformedAt = dpia.PerformedAt,
            NextReviewDueAt = dpia.NextReviewDueAt, ReviewOverdue = ReviewOverdue(dpia.Status, dpia.NextReviewDueAt, now),
            ApprovedAt = dpia.ApprovedAt, ApprovedById = dpia.ApprovedById, ApprovedByName = dpia.ApprovedBy?.Name,
            RetiredAt = dpia.RetiredAt, RetiredById = dpia.RetiredById, RetireReason = dpia.RetireReason,
            Links = dpia.Links.OrderBy(l => l.Kind).ThenBy(l => l.EntityId).Select(l => new DpiaLinkDto
            {
                EntityId = l.EntityId, Kind = l.Kind, EntityName = names.GetValueOrDefault(l.EntityId)
            }).ToList(),
            CreatedAt = dpia.CreatedAt, CreatedById = dpia.CreatedById, UpdatedAt = dpia.UpdatedAt,
            UpdatedById = dpia.UpdatedById
        };
    }

    public async Task<List<AuditLog>> GetDpiaHistoryAsync(int dpiaId, int limit)
    {
        RequireLimit(limit);

        await using var db = DalService.GetContext();
        if (!await db.Dpias.AnyAsync(d => d.Id == dpiaId)) throw DpiaNotFound(dpiaId);

        var links = await db.DpiaLinks.Where(l => l.DpiaId == dpiaId).Select(l => l.Id).ToListAsync();

        return await db.AuditLogs
            .Where(a =>
                (a.EntityType == nameof(Dpia) && a.EntityId == dpiaId) ||
                (a.EntityType == nameof(DpiaLink) && links.Contains(a.EntityId)))
            .Include(a => a.User)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<DpiaDto> CreateDpiaAsync(DpiaRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireGlobalScope(actingUserId, "DataCatalogue.CreateDpia");
        var valid = Validate(request);
        var now = Clock();

        int id;
        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var dpia = new Dpia { Status = DpiaStatus.Draft, CreatedAt = now, CreatedById = actingUserId };
            Apply(dpia, valid);
            db.Dpias.Add(dpia);
            await db.SaveChangesAsync();
            id = dpia.Id;
        }

        Logger.Information("RIPD {Id} drafted by user {User}", id, actingUserId);
        return await GetDpiaAsync(id);
    }

    public async Task<DpiaDto> UpdateDpiaAsync(int dpiaId, DpiaRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireGlobalScope(actingUserId, "DataCatalogue.UpdateDpia");
        var valid = Validate(request);

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var dpia = await db.Dpias.FirstOrDefaultAsync(d => d.Id == dpiaId) ?? throw DpiaNotFound(dpiaId);
            RequireDraft(dpia);

            Apply(dpia, valid);
            dpia.UpdatedAt = Clock();
            dpia.UpdatedById = actingUserId;
            await db.SaveChangesAsync();
        }

        return await GetDpiaAsync(dpiaId);
    }

    public async Task<DpiaDto> LinkDpiaAsync(int dpiaId, int entityId, int actingUserId)
    {
        RequireGlobalScope(actingUserId, "DataCatalogue.LinkDpia");

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var dpia = await db.Dpias.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dpiaId) ?? throw DpiaNotFound(dpiaId);
            RequireDraft(dpia);

            var definition = await db.Entities.AsNoTracking().Where(e => e.Id == entityId)
                                 .Select(e => e.DefinitionName).FirstOrDefaultAsync()
                             ?? throw new DataNotFoundException("entities", entityId.ToString(Invariant));

            if (!DpiaTargets.TryGetValue(definition, out var kind))
                throw new RuleBrokenException(
                    $"A RIPD covers a data record or a business process, not a '{definition}'.", DpiaLinkTargetRule);

            // Idempotent: linking again changes nothing.
            if (!await db.DpiaLinks.AnyAsync(l => l.DpiaId == dpiaId && l.EntityId == entityId))
            {
                db.DpiaLinks.Add(new DpiaLink
                {
                    DpiaId = dpiaId, EntityId = entityId, Kind = kind, CreatedAt = Clock(), CreatedById = actingUserId
                });
                await db.SaveChangesAsync();
            }
        }

        return await GetDpiaAsync(dpiaId);
    }

    public async Task UnlinkDpiaAsync(int dpiaId, int entityId, int actingUserId)
    {
        RequireGlobalScope(actingUserId, "DataCatalogue.UnlinkDpia");

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var dpia = await db.Dpias.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dpiaId) ?? throw DpiaNotFound(dpiaId);
        RequireDraft(dpia);

        var link = await db.DpiaLinks.FirstOrDefaultAsync(l => l.DpiaId == dpiaId && l.EntityId == entityId)
                   ?? throw new DataNotFoundException("dpia_links", $"{dpiaId}/{entityId}");

        db.DpiaLinks.Remove(link);
        await db.SaveChangesAsync();
    }

    public async Task<DpiaDto> ApproveDpiaAsync(int dpiaId, int actingUserId)
    {
        RequireGlobalScope(actingUserId, "DataCatalogue.ApproveDpia");

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var dpia = await db.Dpias.Include(d => d.Links).FirstOrDefaultAsync(d => d.Id == dpiaId)
                       ?? throw DpiaNotFound(dpiaId);
            RequireDraft(dpia);

            // The third line assures; it does not approve what it audits (S50 §4.7) — administrators included.
            await ThirdLineGuard.EnsureNotThirdLineAsync(db, actingUserId, "approve a RIPD");

            var missing = new List<string>();
            if (dpia.PerformedAt is null) missing.Add("the date it was performed");
            if (dpia.ResidualRisk is null) missing.Add("the residual risk it concluded");
            if (string.IsNullOrWhiteSpace(dpia.Summary) && string.IsNullOrWhiteSpace(dpia.DocumentReference))
                missing.Add("a summary or a reference to the document");
            if (dpia.Links.All(l => l.Kind != DpiaLinkKind.DataRecord)) missing.Add("a data record it covers");
            if (dpia.NextReviewDueAt is { } review && review <= Clock())
                missing.Add("a next review date that has not passed already");

            if (missing.Count > 0)
                throw new RuleBrokenException($"The RIPD cannot be approved without {string.Join(", ", missing)}.",
                    DpiaIncompleteRule);

            var now = Clock();
            dpia.Status = DpiaStatus.Approved;
            dpia.ApprovedAt = now;
            dpia.ApprovedById = actingUserId;
            dpia.UpdatedAt = now;
            dpia.UpdatedById = actingUserId;
            await db.SaveChangesAsync();
        }

        Logger.Information("RIPD {Id} approved by user {User}", dpiaId, actingUserId);
        return await GetDpiaAsync(dpiaId);
    }

    public async Task<DpiaDto> RetireDpiaAsync(int dpiaId, DpiaRetireRequest request, int actingUserId)
    {
        RequireGlobalScope(actingUserId, "DataCatalogue.RetireDpia");

        var reason = Required(request?.Reason, DataCatalogueLimits.MaxRetireReasonLength, nameof(DpiaRetireRequest.Reason),
            "Retiring a RIPD needs a reason.");
        if (reason.Length < DataCatalogueLimits.MinRetireReasonLength)
            throw new InvalidParameterException(nameof(DpiaRetireRequest.Reason),
                $"The reason is at least {DataCatalogueLimits.MinRetireReasonLength} characters.");

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var dpia = await db.Dpias.FirstOrDefaultAsync(d => d.Id == dpiaId) ?? throw DpiaNotFound(dpiaId);
            if (dpia.Status == DpiaStatus.Retired)
                throw new RuleBrokenException($"RIPD #{dpiaId} is already retired.", DpiaRetiredRule);

            var now = Clock();
            dpia.Status = DpiaStatus.Retired;
            dpia.RetiredAt = now;
            dpia.RetiredById = actingUserId;
            dpia.RetireReason = reason;
            dpia.UpdatedAt = now;
            dpia.UpdatedById = actingUserId;
            await db.SaveChangesAsync();
        }

        Logger.Warning("RIPD {Id} retired by user {User}", dpiaId, actingUserId);
        return await GetDpiaAsync(dpiaId);
    }

    // --- the requirements of a risk (T209) ---------------------------------------------------------------------------

    public async Task<RiskComplianceDto> GetRiskComplianceAsync(int riskId)
    {
        await using var db = DalService.GetContext();
        await RequireVisibleRiskAsync(db, riskId);

        var links = await db.RiskLegalRequirements.AsNoTracking()
            .Include(l => l.LegalRequirement)
            .Where(l => l.RiskId == riskId)
            .ToListAsync();

        // The data records the risk reaches through its chain — through the scoped context, so a link to something the
        // caller cannot see is not followed.
        var linked = await db.RiskChainLinks.AsNoTracking()
            .Where(l => l.RiskId == riskId && l.EntityId != null)
            .Select(l => l.EntityId!.Value)
            .Distinct()
            .ToListAsync();
        var dataNodes = await db.Entities.AsNoTracking()
            .Where(e => linked.Contains(e.Id) && e.DefinitionName == RiskChainSchema.DataDefinition)
            .Select(e => e.Id)
            .ToListAsync();

        var world = await LoadWorldAsync(dataNodes);
        var now = Clock();

        var records = dataNodes.Select(id =>
        {
            world.Entries.TryGetValue(id, out var entry);
            return new RiskDataRecordDto
            {
                EntityId = id, Name = world.Names.GetValueOrDefault(id), Catalogued = entry is not null,
                PersonalData = entry?.PersonalData, InvolvesMinors = entry?.InvolvesMinors,
                LargeVolume = entry?.LargeVolume ?? false, StrategicResearch = entry?.StrategicResearch ?? false,
                FindingCodes = DataCatalogueFindingsEvaluator.Evaluate(world.FactsOf(id), now).Select(f => f.Code).ToList(),
                CitedRequirementIds = Cited(entry).Select(r => r.Id).Distinct().OrderBy(r => r).ToList()
            };
        }).OrderBy(r => r.Name ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(r => r.EntityId).ToList();

        var direct = links.Select(l => l.LegalRequirementId).ToHashSet();

        return new RiskComplianceDto
        {
            RiskId = riskId,
            Requirements = links
                .OrderBy(l => l.LegalRequirement.Code, StringComparer.OrdinalIgnoreCase)
                .Select(l => new RiskRequirementDto
                {
                    RequirementId = l.LegalRequirementId, Code = l.LegalRequirement.Code, Title = l.LegalRequirement.Title,
                    Kind = l.LegalRequirement.Kind, Note = l.Note, LinkedAt = l.CreatedAt, LinkedById = l.CreatedById
                })
                .ToList(),
            DataRecords = records,
            CatalogueRequirements = dataNodes
                .SelectMany(id => Cited(world.Entries.GetValueOrDefault(id)))
                .Where(r => !direct.Contains(r.Id))
                .GroupBy(r => r.Id).Select(g => Ref(g.First())!)
                .OrderBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public async Task<RiskComplianceDto> LinkRiskRequirementAsync(int riskId, int requirementId,
        RiskLegalRequirementRequest request, int actingUserId)
    {
        var note = Optional(request?.Note, DataCatalogueLimits.MaxRiskLinkNoteLength,
            nameof(RiskLegalRequirementRequest.Note));

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            await RequireVisibleRiskAsync(db, riskId);
            if (!await db.LegalRequirements.AnyAsync(r => r.Id == requirementId)) throw RequirementNotFound(requirementId);

            var link = await db.RiskLegalRequirements
                .FirstOrDefaultAsync(l => l.RiskId == riskId && l.LegalRequirementId == requirementId);
            if (link is null)
            {
                db.RiskLegalRequirements.Add(new RiskLegalRequirement
                {
                    RiskId = riskId, LegalRequirementId = requirementId, Note = note, CreatedAt = Clock(),
                    CreatedById = actingUserId
                });
            }
            else
            {
                // Idempotent: linking again only restates the note.
                link.Note = note;
            }

            await db.SaveChangesAsync();
        }

        return await GetRiskComplianceAsync(riskId);
    }

    public async Task UnlinkRiskRequirementAsync(int riskId, int requirementId, int actingUserId)
    {
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireVisibleRiskAsync(db, riskId);

        var link = await db.RiskLegalRequirements
                       .FirstOrDefaultAsync(l => l.RiskId == riskId && l.LegalRequirementId == requirementId)
                   ?? throw new DataNotFoundException("risk_legal_requirements", $"{riskId}/{requirementId}");

        db.RiskLegalRequirements.Remove(link);
        await db.SaveChangesAsync();
    }

    // --- the world the findings read -----------------------------------------------------------------------------------

    private sealed record DpiaRow(int Id, string Title, DpiaStatus Status, DpiaResidualRisk? ResidualRisk,
        DateTime? ApprovedAt, DateTime? NextReviewDueAt);

    private sealed class RecordWorld
    {
        public Dictionary<int, DataCatalogueEntry> Entries { get; } = new();
        public Dictionary<int, string?> Names { get; set; } = new();

        /// <summary>Per data record, the third parties linked to it or to its group — unscoped.</summary>
        public Dictionary<int, List<(int ThirdPartyId, bool ThroughGroup)>> ProcessorLinks { get; } = new();

        /// <summary>Per live (active or exiting) third party, the countries it keeps the data in or reaches it from.</summary>
        public Dictionary<int, List<string>> LiveProcessorCountries { get; } = new();

        public Dictionary<int, List<DpiaRow>> Dpias { get; } = new();

        public List<DpiaRow> DpiasOf(int node) => Dpias.GetValueOrDefault(node) ?? [];

        public IEnumerable<string> ProcessorCountriesOf(int node) =>
            (ProcessorLinks.GetValueOrDefault(node) ?? [])
            .Select(p => p.ThirdPartyId).Distinct()
            .SelectMany(id => LiveProcessorCountries.GetValueOrDefault(id) ?? []);

        public DataRecordFacts FactsOf(int node)
        {
            if (!Entries.TryGetValue(node, out var entry)) return DataRecordFacts.Uncatalogued;

            return new DataRecordFacts(
                true, entry.PersonalData, entry.InvolvesMinors, entry.LargeVolume, entry.StrategicResearch,
                entry.Purposes.OrderBy(p => p.Id)
                    .Select(p => new PurposeFacts(p.Purpose, p.LegalBasis, p.LegalRequirementId, p.BasisReference)).ToList(),
                entry.RetentionPeriodMonths, entry.RetentionReviewDueAt,
                entry.Locations.Select(l => l.Country).ToList(),
                ProcessorCountriesOf(node).ToList(),
                entry.InternationalTransfer, entry.TransferMechanism,
                DpiasOf(node).Select(d => new DpiaFacts(d.Id, d.Status, d.ResidualRisk, d.NextReviewDueAt)).ToList());
        }
    }

    /// <summary>
    /// Everything the findings of <paramref name="nodes"/> read, through an unscoped context: the catalogue is the
    /// organization's, and the processors' countries must not depend on who asks (S52 D11). Reads only — nothing here is
    /// tracked, so nothing can be written.
    /// </summary>
    private async Task<RecordWorld> LoadWorldAsync(IReadOnlyCollection<int> nodes)
    {
        var world = new RecordWorld();
        if (nodes.Count == 0) return world;

        await using var db = SystemContext();
        var ids = nodes.ToList();

        foreach (var entry in await db.DataCatalogueEntries.AsNoTracking()
                     .Include(e => e.Purposes).ThenInclude(p => p.LegalRequirement)
                     .Include(e => e.Locations)
                     .Include(e => e.RetentionRequirement)
                     .Where(e => ids.Contains(e.EntityId))
                     .ToListAsync())
            world.Entries[entry.EntityId] = entry;

        world.Names = await LoadNamesAsync(db, ids);

        // The processors: a type-3 link to the record or to the group it sits in.
        var parents = await db.Entities.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.Parent != null)
            .Select(e => new { e.Id, Parent = e.Parent!.Value })
            .ToListAsync();
        var parentIds = parents.Select(p => p.Parent).Distinct().ToList();
        var groups = await db.Entities.AsNoTracking()
            .Where(e => parentIds.Contains(e.Id) && e.DefinitionName == RiskChainSchema.DataGroupDefinition)
            .Select(e => e.Id)
            .ToListAsync();
        var groupOf = parents.Where(p => groups.Contains(p.Parent)).ToDictionary(p => p.Id, p => p.Parent);

        var targets = ids.Concat(groupOf.Values).Distinct().ToList();
        var links = await db.ThirdPartyLinks.AsNoTracking()
            .Where(l => l.Kind == ThirdPartyLinkKind.Data && targets.Contains(l.EntityId))
            .Select(l => new { l.ThirdPartyId, l.EntityId })
            .ToListAsync();

        foreach (var node in ids)
        {
            var own = links.Where(l => l.EntityId == node).Select(l => (l.ThirdPartyId, false));
            var viaGroup = groupOf.TryGetValue(node, out var group)
                ? links.Where(l => l.EntityId == group).Select(l => (l.ThirdPartyId, true))
                : [];
            var all = own.Concat(viaGroup).ToList();
            if (all.Count > 0) world.ProcessorLinks[node] = all;
        }

        // Only a live relationship processes data (S51 D6): its locations and its sub-processors' countries.
        var thirdPartyIds = links.Select(l => l.ThirdPartyId).Distinct().ToList();
        if (thirdPartyIds.Count > 0)
        {
            var live = await db.ThirdParties.AsNoTracking()
                .Where(t => thirdPartyIds.Contains(t.Id) &&
                            (t.Status == ThirdPartyStatus.Active || t.Status == ThirdPartyStatus.Exiting))
                .Select(t => t.Id)
                .ToListAsync();
            var locations = await db.ThirdPartyDataLocations.AsNoTracking()
                .Where(l => live.Contains(l.ThirdPartyId))
                .Select(l => new { l.ThirdPartyId, l.Country })
                .ToListAsync();
            var subprocessors = await db.ThirdPartySubprocessors.AsNoTracking()
                .Where(s => live.Contains(s.ThirdPartyId) && s.Country != null)
                .Select(s => new { s.ThirdPartyId, Country = s.Country! })
                .ToListAsync();

            foreach (var id in live)
                world.LiveProcessorCountries[id] = locations.Where(l => l.ThirdPartyId == id).Select(l => l.Country)
                    .Concat(subprocessors.Where(s => s.ThirdPartyId == id).Select(s => s.Country))
                    .ToList();
        }

        foreach (var row in await db.DpiaLinks.AsNoTracking()
                     .Where(l => ids.Contains(l.EntityId))
                     .Select(l => new
                     {
                         l.EntityId, l.Dpia.Id, l.Dpia.Title, l.Dpia.Status, l.Dpia.ResidualRisk, l.Dpia.ApprovedAt,
                         l.Dpia.NextReviewDueAt
                     })
                     .ToListAsync())
        {
            if (!world.Dpias.TryGetValue(row.EntityId, out var list)) world.Dpias[row.EntityId] = list = [];
            list.Add(new DpiaRow(row.Id, row.Title, row.Status, row.ResidualRisk, row.ApprovedAt, row.NextReviewDueAt));
        }

        return world;
    }

    private async Task<List<LegalRequirementDto>> ComposeRequirementsAsync(AuditableContext db,
        IReadOnlyCollection<LegalRequirement> requirements)
    {
        var ids = requirements.Select(r => r.Id).ToList();

        // Risk links through the scoped context: a reader counts the risks they can see.
        var riskLinks = (await db.RiskLegalRequirements.AsNoTracking()
                .Where(l => ids.Contains(l.LegalRequirementId))
                .Select(l => l.LegalRequirementId)
                .ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var purposes = (await db.DataCataloguePurposes.AsNoTracking()
                .Where(p => p.LegalRequirementId != null && ids.Contains(p.LegalRequirementId.Value))
                .Select(p => p.LegalRequirementId!.Value)
                .ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var retentions = (await db.DataCatalogueEntries.AsNoTracking()
                .Where(e => e.RetentionRequirementId != null && ids.Contains(e.RetentionRequirementId.Value))
                .Select(e => e.RetentionRequirementId!.Value)
                .ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        // The counterparty is named only to whoever sees it.
        var thirdPartyIds = requirements.Where(r => r.ThirdPartyId != null).Select(r => r.ThirdPartyId!.Value).Distinct()
            .ToList();
        var visible = await db.ThirdParties.AsNoTracking()
            .Where(t => thirdPartyIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        return requirements.Select(r =>
        {
            var seen = r.ThirdPartyId is { } tp && visible.ContainsKey(tp);
            return new LegalRequirementDto
            {
                Id = r.Id, Code = r.Code, Title = r.Title, Kind = r.Kind, Description = r.Description,
                Reference = r.Reference,
                ThirdPartyId = seen ? r.ThirdPartyId : null,
                ThirdPartyName = seen ? visible[r.ThirdPartyId!.Value] : null,
                ThirdPartyHidden = r.ThirdPartyId is not null && !seen,
                RiskLinkCount = riskLinks.GetValueOrDefault(r.Id),
                PurposeCount = purposes.GetValueOrDefault(r.Id),
                RetentionCount = retentions.GetValueOrDefault(r.Id),
                CreatedAt = r.CreatedAt, CreatedById = r.CreatedById, UpdatedAt = r.UpdatedAt, UpdatedById = r.UpdatedById
            };
        }).ToList();
    }

    // --- applying ------------------------------------------------------------------------------------------------------

    private static void Apply(DataCatalogueEntry entry, DataCatalogueEntryRequest valid)
    {
        entry.PersonalData = valid.PersonalData;
        entry.InvolvesMinors = valid.InvolvesMinors;
        entry.LargeVolume = valid.LargeVolume;
        entry.StrategicResearch = valid.StrategicResearch;
        entry.DataSubjects = valid.DataSubjects;
        entry.DataCategories = valid.DataCategories;
        entry.RetentionPeriodMonths = valid.RetentionPeriodMonths;
        entry.RetentionTrigger = valid.RetentionTrigger;
        entry.RetentionBasis = valid.RetentionBasis;
        entry.RetentionRequirementId = valid.RetentionRequirementId;
        entry.RetentionReviewDueAt = valid.RetentionReviewDueAt;
        entry.RetentionReviewedAt = valid.RetentionReviewedAt;
        entry.InternationalTransfer = valid.InternationalTransfer;
        entry.TransferMechanism = valid.TransferMechanism;
        entry.Notes = valid.Notes;
    }

    /// <summary>Replaces the purposes whole; a purpose of the same text keeps its id and its trail.</summary>
    private static void SyncPurposes(AuditableContext db, DataCatalogueEntry entry, List<DataCataloguePurposeRequest> wanted,
        DateTime now, int actingUserId)
    {
        foreach (var stale in entry.Purposes
                     .Where(p => !wanted.Any(w => PurposeComparer.Equals(w.Purpose, p.Purpose)))
                     .ToList())
        {
            entry.Purposes.Remove(stale);
            db.DataCataloguePurposes.Remove(stale);
        }

        foreach (var want in wanted)
        {
            var row = entry.Purposes.FirstOrDefault(p => PurposeComparer.Equals(p.Purpose, want.Purpose));
            if (row is null)
            {
                row = new DataCataloguePurpose { Purpose = want.Purpose!, CreatedAt = now, CreatedById = actingUserId };
                entry.Purposes.Add(row);
            }

            row.Purpose = want.Purpose!;
            row.LegalBasis = want.LegalBasis;
            row.LegalRequirementId = want.LegalRequirementId;
            row.BasisReference = want.BasisReference;
        }
    }

    /// <summary>Replaces the locations whole; a location of the same country and use keeps its id and its trail.</summary>
    private static void SyncLocations(AuditableContext db, DataCatalogueEntry entry, List<DataCatalogueLocationRequest> wanted,
        DateTime now, int actingUserId)
    {
        foreach (var stale in entry.Locations
                     .Where(l => !wanted.Any(w => w.Country == l.Country && w.Purpose == l.Purpose))
                     .ToList())
        {
            entry.Locations.Remove(stale);
            db.DataCatalogueLocations.Remove(stale);
        }

        foreach (var want in wanted)
        {
            var row = entry.Locations.FirstOrDefault(l => l.Country == want.Country && l.Purpose == want.Purpose);
            if (row is null)
            {
                row = new DataCatalogueLocation
                {
                    Country = want.Country!, Purpose = want.Purpose!.Value, CreatedAt = now, CreatedById = actingUserId
                };
                entry.Locations.Add(row);
            }

            row.Region = want.Region;
        }
    }

    private static void Apply(LegalRequirement requirement, LegalRequirementRequest valid)
    {
        requirement.Code = valid.Code!;
        requirement.Title = valid.Title!;
        requirement.Kind = valid.Kind!.Value;
        requirement.Description = valid.Description;
        requirement.Reference = valid.Reference;
        requirement.ThirdPartyId = valid.ThirdPartyId;
    }

    private static void Apply(Dpia dpia, DpiaRequest valid)
    {
        dpia.Title = valid.Title!;
        dpia.Summary = valid.Summary;
        dpia.DocumentReference = valid.DocumentReference;
        dpia.ResidualRisk = valid.ResidualRisk;
        dpia.PerformedAt = valid.PerformedAt;
        dpia.NextReviewDueAt = valid.NextReviewDueAt;
    }

    // --- validation ----------------------------------------------------------------------------------------------------

    /// <summary>Validates and normalizes a catalogue request; the copy returned carries trimmed, upper-cased, UTC values.</summary>
    private DataCatalogueEntryRequest Validate(DataCatalogueEntryRequest request)
    {
        var now = Clock();

        var valid = new DataCatalogueEntryRequest
        {
            PersonalData = request.PersonalData,
            InvolvesMinors = request.InvolvesMinors,
            LargeVolume = request.LargeVolume,
            StrategicResearch = request.StrategicResearch,
            DataSubjects = Kind(request.DataSubjects, DataCatalogueLimits.MaxDataSubjectsLength,
                nameof(DataCatalogueEntryRequest.DataSubjects)),
            DataCategories = Kind(request.DataCategories, DataCatalogueLimits.MaxDataCategoriesLength,
                nameof(DataCatalogueEntryRequest.DataCategories)),
            RetentionPeriodMonths = request.RetentionPeriodMonths,
            RetentionTrigger = Kind(request.RetentionTrigger, DataCatalogueLimits.MaxRetentionTriggerLength,
                nameof(DataCatalogueEntryRequest.RetentionTrigger)),
            RetentionBasis = Kind(request.RetentionBasis, DataCatalogueLimits.MaxRetentionBasisLength,
                nameof(DataCatalogueEntryRequest.RetentionBasis)),
            RetentionRequirementId = request.RetentionRequirementId,
            RetentionReviewDueAt = Utc(request.RetentionReviewDueAt),
            RetentionReviewedAt = Utc(request.RetentionReviewedAt),
            InternationalTransfer = request.InternationalTransfer,
            TransferMechanism = request.TransferMechanism,
            Notes = Kind(request.Notes, DataCatalogueLimits.MaxNotesLength, nameof(DataCatalogueEntryRequest.Notes))
        };

        if (valid.PersonalData is { } category && !Enum.IsDefined(category))
            throw new InvalidParameterException(nameof(DataCatalogueEntryRequest.PersonalData),
                "The category is not personal (1), personal (2), sensitive personal (3) or anonymised (4).");

        if (valid.RetentionPeriodMonths is < 0 or > DataCatalogueLimits.MaxRetentionMonths)
            throw new InvalidParameterException(nameof(DataCatalogueEntryRequest.RetentionPeriodMonths),
                $"The retention period is 0 to {DataCatalogueLimits.MaxRetentionMonths} months.");

        if (valid.RetentionRequirementId is <= 0)
            throw new InvalidParameterException(nameof(DataCatalogueEntryRequest.RetentionRequirementId),
                "The retention requirement is a requirement id.");

        if (valid.RetentionReviewedAt > now)
            throw new InvalidParameterException(nameof(DataCatalogueEntryRequest.RetentionReviewedAt),
                "A review cannot be in the future.");

        if (valid.TransferMechanism is { } mechanism && !Enum.IsDefined(mechanism))
            throw new InvalidParameterException(nameof(DataCatalogueEntryRequest.TransferMechanism),
                "The mechanism is one of the safeguards of LGPD art. 33 (1 to 12).");

        if (valid.TransferMechanism is not null && valid.InternationalTransfer != true)
            throw new InvalidParameterException(nameof(DataCatalogueEntryRequest.TransferMechanism),
                "A transfer mechanism needs a declared international transfer.");

        valid.Purposes = ValidatePurposes(request.Purposes);
        valid.Locations = ValidateLocations(request.Locations);
        return valid;
    }

    private static List<DataCataloguePurposeRequest> ValidatePurposes(List<DataCataloguePurposeRequest>? items)
    {
        const string list = nameof(DataCatalogueEntryRequest.Purposes);
        if (items is null)
            throw new InvalidParameterException(list, "The purposes are required — send an empty list to declare none.");
        if (items.Count > DataCatalogueLimits.MaxPurposes)
            throw new InvalidParameterException(list, $"At most {DataCatalogueLimits.MaxPurposes} purposes.");

        var seen = new HashSet<string>(PurposeComparer);
        var valid = new List<DataCataloguePurposeRequest>();
        foreach (var item in items)
        {
            if (item is null) throw new InvalidParameterException(list, "An entry is empty.");

            var purpose = Required(item.Purpose, DataCatalogueLimits.MaxPurposeLength,
                nameof(DataCataloguePurposeRequest.Purpose), "Every purpose needs a description.");
            RefusePersonalValue(purpose, nameof(DataCataloguePurposeRequest.Purpose));
            if (!seen.Add(purpose))
                throw new InvalidParameterException(nameof(DataCataloguePurposeRequest.Purpose), "A purpose is listed twice.");

            if (item.LegalBasis is { } basis && !Enum.IsDefined(basis))
                throw new InvalidParameterException(nameof(DataCataloguePurposeRequest.LegalBasis),
                    "The legal basis is an inciso of LGPD art. 7º (1–10) or art. 11 (11–18).");

            if (item.LegalRequirementId is <= 0)
                throw new InvalidParameterException(nameof(DataCataloguePurposeRequest.LegalRequirementId),
                    "The requirement is a requirement id.");

            valid.Add(new DataCataloguePurposeRequest
            {
                Purpose = purpose, LegalBasis = item.LegalBasis, LegalRequirementId = item.LegalRequirementId,
                BasisReference = Kind(item.BasisReference, DataCatalogueLimits.MaxBasisReferenceLength,
                    nameof(DataCataloguePurposeRequest.BasisReference))
            });
        }

        return valid;
    }

    private static List<DataCatalogueLocationRequest> ValidateLocations(List<DataCatalogueLocationRequest>? items)
    {
        const string list = nameof(DataCatalogueEntryRequest.Locations);
        if (items is null)
            throw new InvalidParameterException(list, "The locations are required — send an empty list to declare none.");
        if (items.Count > DataCatalogueLimits.MaxLocations)
            throw new InvalidParameterException(list, $"At most {DataCatalogueLimits.MaxLocations} locations.");

        var seen = new HashSet<(string, DataLocationPurpose)>();
        var valid = new List<DataCatalogueLocationRequest>();
        foreach (var item in items)
        {
            if (item is null) throw new InvalidParameterException(list, "An entry is empty.");

            var country = Country(item.Country)
                          ?? throw new InvalidParameterException(nameof(DataCatalogueLocationRequest.Country),
                              "Every location needs a country.");

            if (item.Purpose is not { } purpose || !Enum.IsDefined(purpose))
                throw new InvalidParameterException(nameof(DataCatalogueLocationRequest.Purpose),
                    "The use is storage (1), processing (2), backup (3) or support access (4).");

            if (!seen.Add((country, purpose)))
                throw new InvalidParameterException(nameof(DataCatalogueLocationRequest.Country),
                    $"{country} is listed twice for the same use.");

            valid.Add(new DataCatalogueLocationRequest
            {
                Country = country, Purpose = purpose,
                Region = Kind(item.Region, DataCatalogueLimits.MaxRegionLength, nameof(DataCatalogueLocationRequest.Region))
            });
        }

        return valid;
    }

    private static LegalRequirementRequest Validate(LegalRequirementRequest request)
    {
        var valid = new LegalRequirementRequest
        {
            Code = Required(request.Code, DataCatalogueLimits.MaxRequirementCodeLength, nameof(LegalRequirementRequest.Code),
                "The requirement needs a code."),
            Title = Required(request.Title, DataCatalogueLimits.MaxRequirementTitleLength,
                nameof(LegalRequirementRequest.Title), "The requirement needs a title."),
            Kind = request.Kind,
            Description = Optional(request.Description, DataCatalogueLimits.MaxRequirementDescriptionLength,
                nameof(LegalRequirementRequest.Description)),
            Reference = Optional(request.Reference, DataCatalogueLimits.MaxRequirementReferenceLength,
                nameof(LegalRequirementRequest.Reference)),
            ThirdPartyId = request.ThirdPartyId
        };

        if (valid.Kind is not { } kind || !Enum.IsDefined(kind))
            throw new InvalidParameterException(nameof(LegalRequirementRequest.Kind),
                "The kind is a law (1), a regulation (2), a contract (3) or an internal norm (4).");

        if (valid.ThirdPartyId is <= 0)
            throw new InvalidParameterException(nameof(LegalRequirementRequest.ThirdPartyId), "The third party is a third-party id.");

        if (valid.ThirdPartyId is not null && kind != LegalRequirementKind.Contract)
            throw new InvalidParameterException(nameof(LegalRequirementRequest.ThirdPartyId),
                "Only a contract names a third party as its counterparty.");

        return valid;
    }

    private DpiaRequest Validate(DpiaRequest request)
    {
        var now = Clock();

        var valid = new DpiaRequest
        {
            Title = Required(request.Title, DataCatalogueLimits.MaxDpiaTitleLength, nameof(DpiaRequest.Title),
                "The RIPD needs a title."),
            Summary = Optional(request.Summary, DataCatalogueLimits.MaxDpiaSummaryLength, nameof(DpiaRequest.Summary)),
            DocumentReference = Optional(request.DocumentReference, DataCatalogueLimits.MaxDpiaDocumentReferenceLength,
                nameof(DpiaRequest.DocumentReference)),
            ResidualRisk = request.ResidualRisk,
            PerformedAt = Utc(request.PerformedAt),
            NextReviewDueAt = Utc(request.NextReviewDueAt)
        };

        if (valid.ResidualRisk is { } risk && !Enum.IsDefined(risk))
            throw new InvalidParameterException(nameof(DpiaRequest.ResidualRisk),
                "The residual risk is low (1), medium (2) or high (3).");

        if (valid.PerformedAt > now)
            throw new InvalidParameterException(nameof(DpiaRequest.PerformedAt), "It cannot have been performed in the future.");

        if (valid.PerformedAt is { } performed && valid.NextReviewDueAt is { } review && review <= performed)
            throw new InvalidParameterException(nameof(DpiaRequest.NextReviewDueAt),
                "The next review comes after the RIPD was performed.");

        return valid;
    }

    // --- guards --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes to the catalogue, the requirements and the RIPDs need global scope; refused before any context is opened, so
    /// an existing record and a missing one get the same refusal (S52 D10, the S43 §6 precedent).
    /// </summary>
    private void RequireGlobalScope(int actingUserId, string operation)
    {
        if (!DalService.GetCurrentEntityScope().IsUnrestricted)
            throw new PermissionInvalidException(ContinuityAccess.GlobalScope, actingUserId, operation);
    }

    /// <summary>The node exists (404) and is a data record (422 <c>data_catalogue_target</c>).</summary>
    private static async Task RequireDataRecordAsync(AuditableContext db, int entityId)
    {
        var definition = await db.Entities.AsNoTracking().Where(e => e.Id == entityId).Select(e => e.DefinitionName)
                             .FirstOrDefaultAsync()
                         ?? throw new DataNotFoundException("entities", entityId.ToString(Invariant));

        if (definition != RiskChainSchema.DataDefinition)
            throw new RuleBrokenException(
                $"The LGPD catalogue describes a data record ('{RiskChainSchema.DataDefinition}'), not a '{definition}'.",
                TargetRule);
    }

    private static async Task RequireVisibleRiskAsync(AuditableContext db, int riskId)
    {
        if (!await db.Risks.AsNoTracking().AnyAsync(r => r.Id == riskId))
            throw new DataNotFoundException("risks", riskId.ToString(Invariant));
    }

    private static async Task RequireThirdPartyAsync(AuditableContext db, int? thirdPartyId)
    {
        if (thirdPartyId is { } id && !await db.ThirdParties.AsNoTracking().AnyAsync(t => t.Id == id))
            throw new DataNotFoundException("third_parties", id.ToString(Invariant));
    }

    private static async Task RequireUniqueCodeAsync(AuditableContext db, string code, int? exceptId)
    {
        var lowered = code.ToLowerInvariant();
        if (await db.LegalRequirements.AnyAsync(r => r.Code.ToLower() == lowered && (exceptId == null || r.Id != exceptId)))
            throw CodeTaken(code);
    }

    private static async Task SaveOrConflictAsync(AuditableContext db, string code)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ContinuityService.Mentions(ex, "uq_legal_requirements_code"))
        {
            throw CodeTaken(code);
        }
    }

    private static DataAlreadyExistsException CodeTaken(string code) =>
        new("netrisk", "legal_requirements", code, $"A requirement with the code '{code}' is already catalogued.");

    private static void RequireDraft(Dpia dpia)
    {
        if (dpia.Status == DpiaStatus.Retired)
            throw new RuleBrokenException($"RIPD #{dpia.Id} is retired: it is kept as evidence and changes no more.",
                DpiaRetiredRule);

        if (dpia.Status != DpiaStatus.Draft)
            throw new RuleBrokenException(
                $"RIPD #{dpia.Id} is approved and frozen: review it by drafting a new one and retiring this one.",
                DpiaNotDraftRule);
    }

    private static void RequireLimit(int limit)
    {
        if (limit is < 1 or > DataCatalogueLimits.MaxHistoryLimit)
            throw new InvalidParameterException(nameof(limit), $"The limit is 1 to {DataCatalogueLimits.MaxHistoryLimit}.");
    }

    private static DataNotFoundException RequirementNotFound(int id) => new("legal_requirements", id.ToString(Invariant));

    private static DataNotFoundException DpiaNotFound(int id) => new("dpias", id.ToString(Invariant));

    private static bool ReviewOverdue(DpiaStatus status, DateTime? nextReviewDueAt, DateTime now) =>
        status == DpiaStatus.Approved && nextReviewDueAt is { } due && due < now;

    // --- small helpers -------------------------------------------------------------------------------------------------

    private static IEnumerable<LegalRequirement> Cited(DataCatalogueEntry? entry) =>
        entry is null
            ? []
            : entry.Purposes.Where(p => p.LegalRequirement is not null).Select(p => p.LegalRequirement!)
                .Concat(entry.RetentionRequirement is { } r ? [r] : []);

    private static LegalRequirementRefDto? Ref(LegalRequirement? r) =>
        r is null ? null : new LegalRequirementRefDto { Id = r.Id, Code = r.Code, Title = r.Title, Kind = r.Kind };

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

    /// <summary>A free text that describes a kind of data — trimmed, bounded, and refused if it carries a personal value.</summary>
    private static string? Kind(string? text, int max, string parameter)
    {
        var value = Optional(text, max, parameter);
        RefusePersonalValue(value, parameter);
        return value;
    }

    /// <summary>Refuses, without echoing it, a text that looks like it carries an e-mail address or a CPF (S52 D13).</summary>
    private static void RefusePersonalValue(string? text, string parameter)
    {
        if (PersonalValueGuard.LooksLikePersonalValue(text))
            throw new InvalidParameterException(parameter,
                "The catalogue describes kinds of data, not data: this text looks like it carries an e-mail address or a " +
                "CPF. Describe the kind instead (\"institutional e-mail\", \"CPF\").");
    }

    private static string? Country(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var upper = code.Trim().ToUpperInvariant();
        if (!CountryCode.IsMatch(upper))
            throw new InvalidParameterException(nameof(DataCatalogueLocationRequest.Country),
                "The country is an ISO 3166-1 alpha-2 code.");

        return upper;
    }

    private static string Required(string? text, int max, string parameter, string missing)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidParameterException(parameter, missing);

        var trimmed = text.Trim();
        if (trimmed.Length > max) throw new InvalidParameterException(parameter, $"At most {max} characters.");

        return trimmed;
    }

    private static string? Optional(string? text, int max, string parameter)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim();
        if (trimmed.Length > max) throw new InvalidParameterException(parameter, $"At most {max} characters.");

        return trimmed;
    }

    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { } v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
    };

    private AuditableContext SystemContext() => DalService.GetContext(withIdentity: false, bypassEntityScope: true);
}

/// <summary>
/// What cites a legal requirement (S52 §4.9) — the registry to extend when a new table names one, as
/// <c>ThirdPartyReferences</c> is for a supplier. A use missed here does not corrupt anything: the foreign key's
/// <c>RESTRICT</c> still refuses the delete, as a 500 instead of a sentence.
/// </summary>
public static class LegalRequirementReferences
{
    public sealed record Counts(int RiskLinks, int Purposes, int Retentions)
    {
        public int Total => RiskLinks + Purposes + Retentions;

        public string Describe()
        {
            var parts = new List<string>();
            if (RiskLinks > 0) parts.Add($"linked to {RiskLinks} risk(s)");
            if (Purposes > 0) parts.Add($"cited by {Purposes} purpose(s)");
            if (Retentions > 0) parts.Add($"cited by {Retentions} retention(s)");
            return string.Join(", ", parts);
        }
    }

    /// <summary>Counts every use; pass an unscoped context, so a use the caller cannot see still counts.</summary>
    public static async Task<Counts> CountAsync(AuditableContext db, int requirementId)
    {
        ArgumentNullException.ThrowIfNull(db);

        return new Counts(
            await db.RiskLegalRequirements.CountAsync(l => l.LegalRequirementId == requirementId),
            await db.DataCataloguePurposes.CountAsync(p => p.LegalRequirementId == requirementId),
            await db.DataCatalogueEntries.CountAsync(e => e.RetentionRequirementId == requirementId));
    }
}
