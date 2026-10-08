using System.Globalization;
using System.Text.RegularExpressions;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.ThirdParties;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.Continuity;
using Tools.Risks;
using Tools.Security;
using Tools.ThirdParties;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.10 (S51) — the third-party register: the methodology's discovery front E (third parties: contracts, HECVAT,
/// SBOM, sub-processors, data location, concentration, SLA, RTO/RPO, right to audit, exit and portability), the only
/// front that had no instrument.
///
/// The rules are pure, in <c>Tools.ThirdParties</c>: the HECVAT score (<see cref="HecvatScoring"/>, where a partial
/// questionnaire is incomplete and never a pass), the SBOM parser (<see cref="SbomParser"/>, which treats the document as
/// hostile), the concentration (<see cref="ConcentrationCalculator"/>, which counts a supplier once per dependent critical
/// process) and the findings (<see cref="ThirdPartyFindingsEvaluator"/>). This class loads, guards and stores.
///
/// <b>Scope.</b> Reads go through the caller's scoped context: a third party is seen when it is the organization's
/// (<c>entity_id</c> null) or its entity is in scope, and everything it declares follows it. The concentration of what is
/// listed is computed over the whole organization, unscoped (S51 D8) — a dependency reached through a supplier the
/// reader cannot see is counted, never revealed — so a count never depends on who asks. Writes are guarded by the
/// context's write guard (<c>IEntityScoped</c>): a scoped caller cannot file the organization's supplier nor another
/// entity's.
///
/// <b>Nothing here reaches the network or the disk</b> (S51 D9): an SBOM arrives as the document's text, never as a URL,
/// and is parsed in memory; its file name is metadata. The constructor takes no HTTP client.
/// </summary>
public class ThirdPartiesService(ILogger logger, IDalService dalService, IContinuityService continuity)
    : ServiceBase(logger, dalService), IThirdPartiesService
{
    public const string InUseRule = "third_party_in_use";
    public const string TerminatedRule = "third_party_terminated";
    public const string LinkTargetRule = "third_party_link_target";
    public const string SelfSubprocessorRule = "third_party_self_subprocessor";
    public const string AssessmentVoidedRule = "hecvat_assessment_voided";

    /// <summary>The audit rows <see cref="GetHistoryAsync"/> returns at most.</summary>
    public const int MaxHistoryLimit = 1000;

    /// <summary>
    /// The audited types of Stage 9.10. Their history is served by <c>GET /ThirdParties/{id}/History</c> only, after the
    /// third party is found visible; the generic audit reader refuses them (S51 §4.10), as it refuses the hosts' (S38 §5.4).
    /// </summary>
    public static readonly IReadOnlyCollection<string> TrailTypes =
    [
        nameof(ThirdParty), nameof(ThirdPartyLink), nameof(ThirdPartySubprocessor), nameof(ThirdPartyDataLocation),
        nameof(ThirdPartyAssessment), nameof(ThirdPartySbom)
    ];

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly Regex CountryCode = new("^[A-Z]{2}$", RegexOptions.CultureInvariant);

    /// <summary>A HECVAT question id: "HFIH-01", "DOCU-01", "APPL-12a" upper-cased.</summary>
    private static readonly Regex QuestionIdShape = new("^[A-Z0-9]{1,10}(-[A-Z0-9]{1,8}){0,2}$", RegexOptions.CultureInvariant);

    /// <summary>What a third party may be linked to, and the kind each definition makes (S51 §4.2).</summary>
    private static readonly Dictionary<string, ThirdPartyLinkKind> LinkableDefinitions = new(StringComparer.Ordinal)
    {
        [RiskChainSchema.ItServiceDefinition] = ThirdPartyLinkKind.ItService,
        [RiskChainSchema.ProcessDefinition] = ThirdPartyLinkKind.BusinessProcess,
        [RiskChainSchema.DataDefinition] = ThirdPartyLinkKind.Data,
        [RiskChainSchema.DataGroupDefinition] = ThirdPartyLinkKind.Data
    };

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    // --- reads -------------------------------------------------------------------------------------------------------

    public async Task<List<ThirdPartySummaryDto>> GetThirdPartiesAsync(ThirdPartyStatus? status, bool includeTerminated)
    {
        if (status is { } s && !Enum.IsDefined(s))
            throw new InvalidParameterException(nameof(status), "The status is 1 (prospective) to 4 (terminated).");

        await using var db = DalService.GetContext();

        var ids = await db.ThirdParties.AsNoTracking()
            .Where(t => status == null || t.Status == status)
            .Where(t => includeTerminated || status == ThirdPartyStatus.Terminated || t.Status != ThirdPartyStatus.Terminated)
            .Select(t => t.Id)
            .ToListAsync();

        var world = await LoadWorldAsync();
        var dtos = await ComposeAsync(db, ids, world, detail: false);

        return dtos
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id)
            .Select(d => new ThirdPartySummaryDto
            {
                Id = d.Id, Name = d.Name, Status = d.Status, EntityId = d.EntityId, OwnerId = d.OwnerId,
                IsCloudProvider = d.IsCloudProvider, IsIdentityProvider = d.IsIdentityProvider, ContractEnd = d.ContractEnd,
                HecvatState = d.Hecvat.State, DependentCriticalProcessCount = d.Concentration.DependentCriticalProcessCount,
                FindingCount = d.Findings.Count
            })
            .ToList();
    }

    public async Task<ThirdPartyDto> GetThirdPartyAsync(int thirdPartyId)
    {
        await using var db = DalService.GetContext();
        await RequireVisibleAsync(db, thirdPartyId);

        var world = await LoadWorldAsync();
        return (await ComposeAsync(db, [thirdPartyId], world, detail: true)).Single();
    }

    public async Task<ThirdPartyConcentrationReportDto> GetConcentrationAsync()
    {
        await using var db = DalService.GetContext();

        var visible = await db.ThirdParties.AsNoTracking().Select(t => t.Id).ToListAsync();
        var world = await LoadWorldAsync();

        var entries = visible
            .Where(id => world.Exposures.TryGetValue(id, out var exposure) && exposure.InUse)
            .Select(id => Entry(world, id))
            .ToList();

        return new ThirdPartyConcentrationReportDto
        {
            ComputedAt = Clock(),
            CriticalProcessCount = world.CriticalTotal,
            Suppliers = Dimension(ConcentrationDimension.Supplier, entries, world),
            Cloud = Dimension(ConcentrationDimension.Cloud,
                entries.Where(e => world.Suppliers[e.ThirdPartyId].IsCloudProvider).ToList(), world),
            Identity = Dimension(ConcentrationDimension.Identity,
                entries.Where(e => world.Suppliers[e.ThirdPartyId].IsIdentityProvider).ToList(), world),
            IsScopeRestricted = !DalService.GetCurrentEntityScope().IsUnrestricted
        };
    }

    public async Task<List<EntityThirdPartyDto>> GetByEntityAsync(int entityId)
    {
        await using var db = DalService.GetContext();

        if (!await db.Entities.AsNoTracking().AnyAsync(e => e.Id == entityId))
            throw new DataNotFoundException("entities", entityId.ToString(Invariant));

        var links = await db.ThirdPartyLinks.AsNoTracking()
            .Where(l => l.EntityId == entityId)
            .Select(l => new { l.ThirdPartyId, l.Kind, l.Description })
            .ToListAsync();

        var ids = links.Select(l => l.ThirdPartyId).ToList();
        var parties = await db.ThirdParties.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Status })
            .ToDictionaryAsync(t => t.Id);

        return links
            .Where(l => parties.ContainsKey(l.ThirdPartyId))
            .Select(l => new EntityThirdPartyDto
            {
                ThirdPartyId = l.ThirdPartyId, Name = parties[l.ThirdPartyId].Name, Status = parties[l.ThirdPartyId].Status,
                Kind = l.Kind, Description = l.Description
            })
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.ThirdPartyId)
            .ToList();
    }

    public async Task<ThirdPartyAssessmentDto> GetAssessmentAsync(int thirdPartyId, int assessmentId)
    {
        await using var db = DalService.GetContext();
        await RequireVisibleAsync(db, thirdPartyId);

        var assessment = await db.ThirdPartyAssessments.AsNoTracking()
                             .Include(a => a.Answers)
                             .FirstOrDefaultAsync(a => a.Id == assessmentId && a.ThirdPartyId == thirdPartyId)
                         ?? throw new DataNotFoundException("third_party_assessments", assessmentId.ToString(Invariant));

        return ToDto(assessment, assessment.Answers.Select(Facts).ToList(), includeAnswers: true);
    }

    public async Task<ThirdPartySbomDto> GetSbomAsync(int thirdPartyId, int sbomId)
    {
        await using var db = DalService.GetContext();
        await RequireVisibleAsync(db, thirdPartyId);

        var sbom = await db.ThirdPartySboms.AsNoTracking()
                       .Include(s => s.Components)
                       .FirstOrDefaultAsync(s => s.Id == sbomId && s.ThirdPartyId == thirdPartyId)
                   ?? throw new DataNotFoundException("third_party_sboms", sbomId.ToString(Invariant));

        return ToDto(sbom, includeComponents: true);
    }

    public async Task<List<AuditLog>> GetHistoryAsync(int thirdPartyId, int limit)
    {
        if (limit is < 1 or > MaxHistoryLimit)
            throw new InvalidParameterException(nameof(limit), $"The limit is 1 to {MaxHistoryLimit}.");

        await using var db = DalService.GetContext();
        await RequireVisibleAsync(db, thirdPartyId);

        // What it declares now. The rows of a declaration since removed stay in audit_logs under their own id, which nothing
        // here can find any more — the per-risk trail has the same limit (S51 R5).
        var links = await db.ThirdPartyLinks.Where(l => l.ThirdPartyId == thirdPartyId).Select(l => l.Id).ToListAsync();
        var subprocessors = await db.ThirdPartySubprocessors.Where(x => x.ThirdPartyId == thirdPartyId).Select(x => x.Id)
            .ToListAsync();
        var locations = await db.ThirdPartyDataLocations.Where(x => x.ThirdPartyId == thirdPartyId).Select(x => x.Id)
            .ToListAsync();
        var assessments = await db.ThirdPartyAssessments.Where(x => x.ThirdPartyId == thirdPartyId).Select(x => x.Id)
            .ToListAsync();
        var sboms = await db.ThirdPartySboms.Where(x => x.ThirdPartyId == thirdPartyId).Select(x => x.Id).ToListAsync();

        return await db.AuditLogs
            .Where(a =>
                (a.EntityType == nameof(ThirdParty) && a.EntityId == thirdPartyId) ||
                (a.EntityType == nameof(ThirdPartyLink) && links.Contains(a.EntityId)) ||
                (a.EntityType == nameof(ThirdPartySubprocessor) && subprocessors.Contains(a.EntityId)) ||
                (a.EntityType == nameof(ThirdPartyDataLocation) && locations.Contains(a.EntityId)) ||
                (a.EntityType == nameof(ThirdPartyAssessment) && assessments.Contains(a.EntityId)) ||
                (a.EntityType == nameof(ThirdPartySbom) && sboms.Contains(a.EntityId)))
            .Include(a => a.User)
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync();
    }

    // --- writes: the record ------------------------------------------------------------------------------------------

    public async Task<ThirdPartyDto> CreateAsync(ThirdPartyRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var valid = Validate(request);
        var now = Clock();

        int id;
        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            await RequireEntityAsync(db, valid.EntityId);
            await RequireOwnerAsync(db, valid.OwnerId);
            await RequireUniqueNameAsync(valid.Name!, null);

            var party = new ThirdParty { CreatedAt = now, CreatedById = actingUserId };
            Apply(party, valid, valid.Status ?? ThirdPartyStatus.Prospective, now);
            db.ThirdParties.Add(party);

            // A scoped caller filing the organization's supplier, or another entity's, is refused by the write guard (403).
            await SaveOrConflictAsync(db, valid.Name!);
            id = party.Id;
        }

        Logger.Information("Third party {Id} '{Name}' registered by user {User}", id, valid.Name, actingUserId);
        return await GetThirdPartyAsync(id);
    }

    public async Task<ThirdPartyDto> UpdateAsync(int thirdPartyId, ThirdPartyRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var valid = Validate(request);
        var now = Clock();

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var party = await db.ThirdParties.FirstOrDefaultAsync(t => t.Id == thirdPartyId)
                        ?? throw NotFound(thirdPartyId);

            // The write guard checks where the record goes; this checks where it is. Without it a scoped caller could
            // re-file the organization's supplier under their own unit and so take it over (S51 §4.10).
            RequireWritable(db, party);

            await RequireEntityAsync(db, valid.EntityId);
            // Checked when it changes: an owner since disabled must not block every edit — terminating included.
            if (valid.OwnerId != party.OwnerId) await RequireOwnerAsync(db, valid.OwnerId);
            await RequireUniqueNameAsync(valid.Name!, thirdPartyId);

            var previous = party.Status;
            Apply(party, valid, valid.Status ?? party.Status, now);
            party.UpdatedAt = now;
            party.UpdatedById = actingUserId;

            await SaveOrConflictAsync(db, valid.Name!);

            if (previous != party.Status)
                Logger.Warning("Third party {Id} moved from {Old} to {New} by user {User}", thirdPartyId, previous,
                    party.Status, actingUserId);
        }

        return await GetThirdPartyAsync(thirdPartyId);
    }

    public async Task DeleteAsync(int thirdPartyId, int actingUserId)
    {
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var party = await db.ThirdParties
                        .Include(t => t.Subprocessors)
                        .Include(t => t.DataLocations)
                        .FirstOrDefaultAsync(t => t.Id == thirdPartyId)
                    ?? throw NotFound(thirdPartyId);
        RequireWritable(db, party);

        // Counted unscoped: a row of another entity's supplier naming this one as its sub-processor is a use the caller
        // cannot see, and must still stop the deletion.
        ThirdPartyReferences.Counts references;
        await using (var system = SystemContext())
            references = await ThirdPartyReferences.CountAsync(system, thirdPartyId);

        if (references.Total > 0)
            throw new RuleBrokenException(
                $"'{party.Name}' is in use — {references.Describe()}. End the relationship instead: set its status to " +
                "terminated, which keeps the record as evidence.", InUseRule);

        db.ThirdParties.Remove(party);
        await db.SaveChangesAsync();

        Logger.Warning("Third party {Id} '{Name}' DELETED by user {User}", thirdPartyId, party.Name, actingUserId);
    }

    // --- writes: links (T204) ----------------------------------------------------------------------------------------

    public async Task<ThirdPartyDto> LinkAsync(int thirdPartyId, int entityId, ThirdPartyLinkRequest request,
        int actingUserId)
    {
        var description = Optional(request?.Description, ThirdPartyLimits.MaxLinkDescriptionLength,
            nameof(ThirdPartyLinkRequest.Description));

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var party = await db.ThirdParties.AsNoTracking().FirstOrDefaultAsync(t => t.Id == thirdPartyId)
                        ?? throw NotFound(thirdPartyId);
            RequireWritable(db, party);
            RefuseTerminated(party);

            var definition = await db.Entities.AsNoTracking().Where(e => e.Id == entityId)
                                 .Select(e => e.DefinitionName).FirstOrDefaultAsync()
                             ?? throw new DataNotFoundException("entities", entityId.ToString(Invariant));

            if (!LinkableDefinitions.TryGetValue(definition, out var kind))
                throw new RuleBrokenException(
                    $"A third party is linked to an IT service, a business process or a data record, not to a " +
                    $"'{definition}'.", LinkTargetRule);

            var link = await db.ThirdPartyLinks.FirstOrDefaultAsync(l => l.ThirdPartyId == thirdPartyId && l.EntityId == entityId);
            if (link is null)
            {
                db.ThirdPartyLinks.Add(new ThirdPartyLink
                {
                    ThirdPartyId = thirdPartyId, EntityId = entityId, Kind = kind, Description = description,
                    CreatedAt = Clock(), CreatedById = actingUserId
                });
            }
            else
            {
                // Idempotent: linking again only restates the description.
                link.Description = description;
            }

            await db.SaveChangesAsync();
        }

        return await GetThirdPartyAsync(thirdPartyId);
    }

    public async Task UnlinkAsync(int thirdPartyId, int entityId, int actingUserId)
    {
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireWritablePartyAsync(db, thirdPartyId);

        var link = await db.ThirdPartyLinks.FirstOrDefaultAsync(l => l.ThirdPartyId == thirdPartyId && l.EntityId == entityId)
                   ?? throw new DataNotFoundException("third_party_links", $"{thirdPartyId}/{entityId}");

        db.ThirdPartyLinks.Remove(link);
        await db.SaveChangesAsync();
    }

    // --- writes: declarations ----------------------------------------------------------------------------------------

    public async Task<ThirdPartyDto> SetSubprocessorsAsync(int thirdPartyId, ThirdPartySubprocessorsRequest request,
        int actingUserId)
    {
        var items = request?.Subprocessors
                    ?? throw new InvalidParameterException(nameof(ThirdPartySubprocessorsRequest.Subprocessors),
                        "The list is required — send an empty list to declare that there is none.");
        if (items.Count > ThirdPartyLimits.MaxSubprocessors)
            throw new InvalidParameterException(nameof(ThirdPartySubprocessorsRequest.Subprocessors),
                $"At most {ThirdPartyLimits.MaxSubprocessors} sub-processors.");

        var wanted = new List<ThirdPartySubprocessor>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item is null)
                throw new InvalidParameterException(nameof(ThirdPartySubprocessorsRequest.Subprocessors), "An entry is empty.");

            var name = Required(item.Name, ThirdPartyLimits.MaxSubprocessorNameLength,
                nameof(ThirdPartySubprocessorRequest.Name), "Every sub-processor needs a name.");
            if (!names.Add(name))
                throw new InvalidParameterException(nameof(ThirdPartySubprocessorRequest.Name),
                    $"'{name}' is listed twice.");

            if (item.SubprocessorThirdPartyId is <= 0)
                throw new InvalidParameterException(nameof(ThirdPartySubprocessorRequest.SubprocessorThirdPartyId),
                    "The sub-processor's record is a third-party id.");
            if (item.SubprocessorThirdPartyId == thirdPartyId)
                throw new RuleBrokenException("A third party cannot be its own sub-processor.", SelfSubprocessorRule);

            wanted.Add(new ThirdPartySubprocessor
            {
                Name = name,
                SubprocessorThirdPartyId = item.SubprocessorThirdPartyId,
                Service = Optional(item.Service, ThirdPartyLimits.MaxSubprocessorServiceLength,
                    nameof(ThirdPartySubprocessorRequest.Service)),
                Country = Country(item.Country, nameof(ThirdPartySubprocessorRequest.Country)),
                ProcessesPersonalData = item.ProcessesPersonalData
            });
        }

        var now = Clock();
        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var party = await db.ThirdParties.Include(t => t.Subprocessors).FirstOrDefaultAsync(t => t.Id == thirdPartyId)
                        ?? throw NotFound(thirdPartyId);
            RequireWritable(db, party);
            RefuseTerminated(party);

            ThirdPartySubprocessor? Kept(ThirdPartySubprocessor w) => party.Subprocessors.FirstOrDefault(e =>
                string.Equals(e.Name, w.Name, StringComparison.OrdinalIgnoreCase));

            // A registered sub-processor must be one the caller can see: nobody names what they cannot read — except the
            // one a kept row already names, which another unit's writer may legitimately not see (S51 §4.3).
            var named = wanted
                .Where(w => w.SubprocessorThirdPartyId is { } id && Kept(w)?.SubprocessorThirdPartyId != id)
                .Select(w => w.SubprocessorThirdPartyId!.Value)
                .Distinct().ToList();
            var known = await db.ThirdParties.AsNoTracking().Where(t => named.Contains(t.Id)).Select(t => t.Id).ToListAsync();
            if (named.Except(known).FirstOrDefault() is var missing and > 0)
                throw new DataNotFoundException("third_parties", missing.ToString(Invariant));

            // The stored registered ids the caller cannot see. The DTO shows them as null, so a caller resending the list
            // sends null for them: that means "unchanged", never "erase" — erasing would drop the fourth-party edge from
            // every reader's concentration and make the hidden supplier deletable (S51 D8, §4.9).
            var stored = party.Subprocessors.Where(e => e.SubprocessorThirdPartyId != null)
                .Select(e => e.SubprocessorThirdPartyId!.Value).Distinct().ToList();
            var visibleStored = (await db.ThirdParties.AsNoTracking().Where(t => stored.Contains(t.Id)).Select(t => t.Id)
                .ToListAsync()).ToHashSet();

            // Kept rows keep their id (and their trail); the others are removed and the new ones added.
            foreach (var existing in party.Subprocessors.ToList())
            {
                var match = wanted.FirstOrDefault(w => string.Equals(w.Name, existing.Name, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    db.ThirdPartySubprocessors.Remove(existing);
                    continue;
                }

                var hidden = existing.SubprocessorThirdPartyId is { } current && !visibleStored.Contains(current);

                existing.Name = match.Name;
                existing.SubprocessorThirdPartyId = match.SubprocessorThirdPartyId
                                                    ?? (hidden ? existing.SubprocessorThirdPartyId : null);
                existing.Service = match.Service;
                existing.Country = match.Country;
                existing.ProcessesPersonalData = match.ProcessesPersonalData;
                wanted.Remove(match);
            }

            foreach (var added in wanted)
            {
                added.ThirdPartyId = thirdPartyId;
                added.CreatedAt = now;
                added.CreatedById = actingUserId;
                db.ThirdPartySubprocessors.Add(added);
            }

            party.SubprocessorsDeclaredAt = now;
            party.UpdatedAt = now;
            party.UpdatedById = actingUserId;

            await db.SaveChangesAsync();
        }

        return await GetThirdPartyAsync(thirdPartyId);
    }

    public async Task<ThirdPartyDto> SetDataLocationsAsync(int thirdPartyId, ThirdPartyDataLocationsRequest request,
        int actingUserId)
    {
        var items = request?.Locations
                    ?? throw new InvalidParameterException(nameof(ThirdPartyDataLocationsRequest.Locations),
                        "The list is required.");
        if (items.Count > ThirdPartyLimits.MaxDataLocations)
            throw new InvalidParameterException(nameof(ThirdPartyDataLocationsRequest.Locations),
                $"At most {ThirdPartyLimits.MaxDataLocations} data locations.");

        var wanted = new List<ThirdPartyDataLocation>();
        foreach (var item in items)
        {
            if (item is null)
                throw new InvalidParameterException(nameof(ThirdPartyDataLocationsRequest.Locations), "An entry is empty.");

            var country = Country(item.Country, nameof(ThirdPartyDataLocationRequest.Country))
                          ?? throw new InvalidParameterException(nameof(ThirdPartyDataLocationRequest.Country),
                              "Every location needs its country (ISO 3166-1 alpha-2).");

            if (item.Purpose is not { } purpose || !Enum.IsDefined(purpose))
                throw new InvalidParameterException(nameof(ThirdPartyDataLocationRequest.Purpose),
                    "The purpose is storage (1), processing (2), backup (3) or support access (4).");

            if (wanted.Any(w => w.Country == country && w.Purpose == purpose))
                throw new InvalidParameterException(nameof(ThirdPartyDataLocationRequest.Country),
                    $"{country} for {purpose} is listed twice.");

            wanted.Add(new ThirdPartyDataLocation
            {
                Country = country, Purpose = purpose,
                Region = Optional(item.Region, ThirdPartyLimits.MaxRegionLength, nameof(ThirdPartyDataLocationRequest.Region))
            });
        }

        var now = Clock();
        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var party = await db.ThirdParties.Include(t => t.DataLocations).FirstOrDefaultAsync(t => t.Id == thirdPartyId)
                        ?? throw NotFound(thirdPartyId);
            RequireWritable(db, party);
            RefuseTerminated(party);

            foreach (var existing in party.DataLocations.ToList())
            {
                var match = wanted.FirstOrDefault(w => w.Country == existing.Country && w.Purpose == existing.Purpose);
                if (match is null)
                {
                    db.ThirdPartyDataLocations.Remove(existing);
                    continue;
                }

                existing.Region = match.Region;
                wanted.Remove(match);
            }

            foreach (var added in wanted)
            {
                added.ThirdPartyId = thirdPartyId;
                added.CreatedAt = now;
                added.CreatedById = actingUserId;
                db.ThirdPartyDataLocations.Add(added);
            }

            party.UpdatedAt = now;
            party.UpdatedById = actingUserId;

            await db.SaveChangesAsync();
        }

        return await GetThirdPartyAsync(thirdPartyId);
    }

    // --- writes: HECVAT ----------------------------------------------------------------------------------------------

    public async Task<ThirdPartyAssessmentDto> RecordAssessmentAsync(int thirdPartyId, ThirdPartyAssessmentRequest request,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = Clock();

        if (request.Variant is not { } variant || !Enum.IsDefined(variant))
            throw new InvalidParameterException(nameof(ThirdPartyAssessmentRequest.Variant),
                "The variant is Lite (1), Full (2), On-Premise (3) or the unified HECVAT 3 (4).");

        var version = Required(request.FrameworkVersion, ThirdPartyLimits.MaxFrameworkVersionLength,
            nameof(ThirdPartyAssessmentRequest.FrameworkVersion), "The HECVAT version the vendor answered is required.");

        if (request.ExpectedQuestionCount is not { } expected || expected is < 1 or > ThirdPartyLimits.MaxExpectedQuestions)
            throw new InvalidParameterException(nameof(ThirdPartyAssessmentRequest.ExpectedQuestionCount),
                $"How many questions the vendor was asked is required: 1 to {ThirdPartyLimits.MaxExpectedQuestions}. " +
                "It is what makes a partial answer measurably partial.");

        var responded = Utc(request.RespondedAt);
        if (responded > now)
            throw new InvalidParameterException(nameof(ThirdPartyAssessmentRequest.RespondedAt),
                "The vendor cannot have answered in the future.");

        var validUntil = Utc(request.ValidUntil);
        if (validUntil is { } until && responded is { } at && until <= at)
            throw new InvalidParameterException(nameof(ThirdPartyAssessmentRequest.ValidUntil),
                "The validity must end after the answer.");

        var assessment = new ThirdPartyAssessment
        {
            ThirdPartyId = thirdPartyId,
            Variant = variant,
            FrameworkVersion = version,
            ExpectedQuestionCount = expected,
            RespondedAt = responded,
            ValidUntil = validUntil,
            EvidenceReference = Optional(request.EvidenceReference, ThirdPartyLimits.MaxEvidenceReferenceLength,
                nameof(ThirdPartyAssessmentRequest.EvidenceReference)),
            Notes = Optional(request.Notes, ThirdPartyLimits.MaxAssessmentNotesLength, nameof(ThirdPartyAssessmentRequest.Notes)),
            CreatedAt = now,
            CreatedById = actingUserId
        };

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var party = await db.ThirdParties.AsNoTracking().FirstOrDefaultAsync(t => t.Id == thirdPartyId)
                        ?? throw NotFound(thirdPartyId);
            RequireWritable(db, party);
            RefuseTerminated(party);

            db.ThirdPartyAssessments.Add(assessment);
            await db.SaveChangesAsync();
        }

        Logger.Information("HECVAT {Assessment} ({Variant} {Version}, {Expected} questions) recorded for third party {Id} " +
                           "by user {User}", assessment.Id, variant, version, expected, thirdPartyId, actingUserId);

        return await GetAssessmentAsync(thirdPartyId, assessment.Id);
    }

    public async Task<ThirdPartyAssessmentDto> ReplaceAnswersAsync(int thirdPartyId, int assessmentId,
        ThirdPartyAssessmentAnswersRequest request, int actingUserId)
    {
        var items = request?.Answers
                    ?? throw new InvalidParameterException(nameof(ThirdPartyAssessmentAnswersRequest.Answers),
                        "The answers are required.");
        if (items.Count > ThirdPartyLimits.MaxExpectedQuestions)
            throw new InvalidParameterException(nameof(ThirdPartyAssessmentAnswersRequest.Answers),
                $"At most {ThirdPartyLimits.MaxExpectedQuestions} answers.");

        var wanted = new Dictionary<string, ThirdPartyAssessmentAnswer>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null)
                throw new InvalidParameterException(nameof(ThirdPartyAssessmentAnswersRequest.Answers), "An answer is empty.");

            var questionId = (item.QuestionId ?? string.Empty).Trim().ToUpperInvariant();
            if (questionId.Length is 0 or > ThirdPartyLimits.MaxQuestionIdLength || !QuestionIdShape.IsMatch(questionId))
                throw new InvalidParameterException(nameof(HecvatAnswerRequest.QuestionId),
                    $"'{Shorten(item.QuestionId)}' is not a HECVAT question id (such as HFIH-01).");

            if (wanted.ContainsKey(questionId))
                throw new InvalidParameterException(nameof(HecvatAnswerRequest.QuestionId), $"{questionId} is answered twice.");

            if (item.Answer is not { } answer || !Enum.IsDefined(answer))
                throw new InvalidParameterException(nameof(HecvatAnswerRequest.Answer),
                    $"{questionId}: the answer is Yes (1), No (2), N/A (3) or blank (4).");

            if (item.PreferredAnswer is { } preferred && preferred is not (HecvatAnswer.Yes or HecvatAnswer.No))
                throw new InvalidParameterException(nameof(HecvatAnswerRequest.PreferredAnswer),
                    $"{questionId}: the preferred answer is Yes (1) or No (2), or none for an informational question.");

            var weight = item.Weight ?? ThirdPartyLimits.MinAnswerWeight;
            if (weight is < ThirdPartyLimits.MinAnswerWeight or > ThirdPartyLimits.MaxAnswerWeight)
                throw new InvalidParameterException(nameof(HecvatAnswerRequest.Weight),
                    $"{questionId}: the weight is {ThirdPartyLimits.MinAnswerWeight} to {ThirdPartyLimits.MaxAnswerWeight}.");

            wanted[questionId] = new ThirdPartyAssessmentAnswer
            {
                QuestionId = questionId, Answer = answer, PreferredAnswer = item.PreferredAnswer, Weight = weight,
                Critical = item.Critical,
                Notes = Optional(item.Notes, ThirdPartyLimits.MaxAnswerNotesLength, nameof(HecvatAnswerRequest.Notes))
            };
        }

        var now = Clock();
        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            await RequireWritablePartyAsync(db, thirdPartyId);

            var assessment = await db.ThirdPartyAssessments.Include(a => a.Answers)
                                 .FirstOrDefaultAsync(a => a.Id == assessmentId && a.ThirdPartyId == thirdPartyId)
                             ?? throw new DataNotFoundException("third_party_assessments", assessmentId.ToString(Invariant));

            if (assessment.VoidedAt is not null)
                throw new RuleBrokenException("This assessment was voided; record a new one.", AssessmentVoidedRule);

            if (wanted.Count > assessment.ExpectedQuestionCount)
                throw new InvalidParameterException(nameof(ThirdPartyAssessmentAnswersRequest.Answers),
                    $"{wanted.Count} answers for a questionnaire of {assessment.ExpectedQuestionCount} questions.");

            // Kept by question id, so an unchanged answer is not rewritten; the others are removed or added.
            foreach (var existing in assessment.Answers.ToList())
            {
                if (!wanted.Remove(existing.QuestionId, out var match))
                {
                    db.ThirdPartyAssessmentAnswers.Remove(existing);
                    continue;
                }

                existing.Answer = match.Answer;
                existing.PreferredAnswer = match.PreferredAnswer;
                existing.Weight = match.Weight;
                existing.Critical = match.Critical;
                existing.Notes = match.Notes;
            }

            foreach (var added in wanted.Values)
            {
                added.AssessmentId = assessmentId;
                added.CreatedAt = now;
                db.ThirdPartyAssessmentAnswers.Add(added);
            }

            // The answers are not in the audit allowlist (S51 D12); this row is, so the replacement is in the trail.
            assessment.AnswersUpdatedAt = now;
            assessment.AnswersUpdatedById = actingUserId;

            await db.SaveChangesAsync();
        }

        return await GetAssessmentAsync(thirdPartyId, assessmentId);
    }

    public async Task<ThirdPartyAssessmentDto> VoidAssessmentAsync(int thirdPartyId, int assessmentId,
        ThirdPartyAssessmentVoidRequest request, int actingUserId)
    {
        var reason = request?.Reason?.Trim();
        if (reason is null || reason.Length < ThirdPartyLimits.MinVoidReasonLength
                           || reason.Length > ThirdPartyLimits.MaxVoidReasonLength)
            throw new InvalidParameterException(nameof(ThirdPartyAssessmentVoidRequest.Reason),
                $"Voiding an assessment needs a reason of {ThirdPartyLimits.MinVoidReasonLength} to " +
                $"{ThirdPartyLimits.MaxVoidReasonLength} characters.");

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            await RequireWritablePartyAsync(db, thirdPartyId);

            var assessment = await db.ThirdPartyAssessments
                                 .FirstOrDefaultAsync(a => a.Id == assessmentId && a.ThirdPartyId == thirdPartyId)
                             ?? throw new DataNotFoundException("third_party_assessments", assessmentId.ToString(Invariant));

            if (assessment.VoidedAt is not null)
                throw new RuleBrokenException("This assessment is already void.", AssessmentVoidedRule);

            assessment.VoidedAt = Clock();
            assessment.VoidedById = actingUserId;
            assessment.VoidReason = reason;

            await db.SaveChangesAsync();
        }

        Logger.Warning("HECVAT {Assessment} of third party {Id} VOIDED by user {User}", assessmentId, thirdPartyId, actingUserId);
        return await GetAssessmentAsync(thirdPartyId, assessmentId);
    }

    // --- writes: SBOM ------------------------------------------------------------------------------------------------

    public async Task<ThirdPartySbomDto> ImportSbomAsync(int thirdPartyId, ThirdPartySbomRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var componentName = Optional(request.ComponentName, ThirdPartyLimits.MaxSbomComponentNameLength,
            nameof(ThirdPartySbomRequest.ComponentName));
        var componentVersion = Optional(request.ComponentVersion, ThirdPartyLimits.MaxSbomComponentVersionLength,
            nameof(ThirdPartySbomRequest.ComponentVersion));

        // Before any database work: a hostile document is refused by the parser, which reads it in memory only.
        var parsed = SbomParser.Parse(request.Document);

        componentName ??= parsed.DescribedName
                          ?? throw new InvalidParameterException(nameof(ThirdPartySbomRequest.ComponentName),
                              "Name the supplied component: the document does not describe one.");
        componentVersion ??= parsed.DescribedVersion;

        var now = Clock();
        var sbom = new ThirdPartySbom
        {
            ThirdPartyId = thirdPartyId,
            ComponentName = componentName,
            ComponentVersion = componentVersion,
            Format = parsed.Format,
            SpecVersion = parsed.SpecVersion,
            SerialNumber = parsed.SerialNumber,
            DocumentSha256 = parsed.Sha256,
            DocumentSizeBytes = parsed.SizeBytes,
            FileName = SbomParser.SafeFileName(request.FileName),
            ComponentCount = parsed.Components.Count,
            UploadedAt = now,
            UploadedById = actingUserId,
            Components = parsed.Components.Select(c => new ThirdPartySbomComponent
            {
                Name = c.Name, Version = c.Version, Purl = c.Purl, License = c.License, CreatedAt = now
            }).ToList()
        };

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var party = await db.ThirdParties.AsNoTracking().FirstOrDefaultAsync(t => t.Id == thirdPartyId)
                        ?? throw NotFound(thirdPartyId);
            RequireWritable(db, party);
            RefuseTerminated(party);

            if (await db.ThirdPartySboms.AnyAsync(s => s.ThirdPartyId == thirdPartyId && s.DocumentSha256 == parsed.Sha256))
                throw new DataAlreadyExistsException("netrisk", "third_party_sboms", parsed.Sha256,
                    "This SBOM document is already imported for this supplier.");

            db.ThirdPartySboms.Add(sbom);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ContinuityService.Mentions(ex, "uq_third_party_sboms_third_party_id_document_sha256"))
            {
                throw new DataAlreadyExistsException("netrisk", "third_party_sboms", parsed.Sha256,
                    "This SBOM document is already imported for this supplier.");
            }
        }

        Logger.Information("SBOM {Sbom} ({Format}, {Count} components, sha256 {Hash}) imported for third party {Id} by user " +
                           "{User}", sbom.Id, sbom.Format, sbom.ComponentCount, sbom.DocumentSha256, thirdPartyId, actingUserId);

        return await GetSbomAsync(thirdPartyId, sbom.Id);
    }

    public async Task DeleteSbomAsync(int thirdPartyId, int sbomId, int actingUserId)
    {
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireWritablePartyAsync(db, thirdPartyId);

        var sbom = await db.ThirdPartySboms.Include(s => s.Components)
                       .FirstOrDefaultAsync(s => s.Id == sbomId && s.ThirdPartyId == thirdPartyId)
                   ?? throw new DataNotFoundException("third_party_sboms", sbomId.ToString(Invariant));

        db.ThirdPartySboms.Remove(sbom);
        await db.SaveChangesAsync();

        Logger.Warning("SBOM {Sbom} of third party {Id} REMOVED by user {User}", sbomId, thirdPartyId, actingUserId);
    }

    // --- composition -------------------------------------------------------------------------------------------------

    /// <summary>The organization's suppliers and continuity graph, unscoped, with every supplier's exposure (S51 D8).</summary>
    private sealed record World(
        ContinuityGraph Graph,
        IReadOnlyDictionary<int, SupplierFacts> Suppliers,
        IReadOnlyDictionary<int, SupplierExposure> Exposures,
        int CriticalTotal);

    private async Task<World> LoadWorldAsync()
    {
        var graph = await continuity.GetGraphAsync();

        await using var system = SystemContext();

        var parties = await system.ThirdParties.AsNoTracking()
            .Select(t => new { t.Id, t.Name, t.Status, t.IsCloudProvider, t.IsIdentityProvider })
            .ToListAsync();

        var supplied = (await system.ThirdPartyLinks.AsNoTracking()
                .Where(l => l.Kind == ThirdPartyLinkKind.ItService || l.Kind == ThirdPartyLinkKind.BusinessProcess)
                .Select(l => new { l.ThirdPartyId, l.EntityId })
                .ToListAsync())
            .GroupBy(l => l.ThirdPartyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)g.Select(l => l.EntityId).ToList());

        var subprocessing = await system.ThirdPartySubprocessors.AsNoTracking()
            .Where(s => s.SubprocessorThirdPartyId != null)
            .Select(s => new { s.ThirdPartyId, Sub = s.SubprocessorThirdPartyId!.Value })
            .ToListAsync();

        var suppliers = parties.Select(p => new SupplierFacts(p.Id, p.Name, p.Status, p.IsCloudProvider,
            p.IsIdentityProvider, supplied.GetValueOrDefault(p.Id) ?? Array.Empty<int>())).ToList();

        var exposures = ConcentrationCalculator.Compute(graph, suppliers,
            subprocessing.Select(s => (s.ThirdPartyId, s.Sub)).ToList());

        return new World(graph, suppliers.ToDictionary(s => s.Id), exposures, ConcentrationCalculator.CriticalProcessCount(graph));
    }

    private ConcentrationEntryDto Entry(World world, int thirdPartyId)
    {
        var exposure = world.Exposures.GetValueOrDefault(thirdPartyId) ?? new SupplierExposure();
        var supplier = world.Suppliers.GetValueOrDefault(thirdPartyId);

        return new ConcentrationEntryDto
        {
            ThirdPartyId = thirdPartyId,
            Name = supplier?.Name ?? string.Empty,
            DependentCriticalProcessCount = exposure.CriticalProcessIds.Count,
            Share = ConcentrationCalculator.Share(exposure.CriticalProcessIds.Count, world.CriticalTotal),
            CriticalProcesses = exposure.CriticalProcessIds.Take(ThirdPartyLimits.MaxListedProcesses)
                .Select(id => new CriticalProcessRefDto { EntityId = id, Name = world.Graph.Find(id)?.Name })
                .ToList(),
            ReachedThroughSubprocessing = exposure.ReachedThroughSubprocessing,
            LinksWithoutDeclaredDependents = exposure.LinksWithoutDeclaredDependents
        };
    }

    private static ConcentrationDimensionDto Dimension(ConcentrationDimension dimension, List<ConcentrationEntryDto> entries,
        World world)
    {
        var ordered = entries
            .OrderByDescending(e => e.DependentCriticalProcessCount)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.ThirdPartyId)
            .ToList();
        var top = ordered.FirstOrDefault();

        return new ConcentrationDimensionDto
        {
            Dimension = dimension,
            ProviderCount = ordered.Count,
            MaxShare = top is null || world.CriticalTotal == 0 ? null : top.Share,
            MostConcentratedThirdPartyId = top is { DependentCriticalProcessCount: > 0 } ? top.ThirdPartyId : null,
            Entries = ordered
        };
    }

    /// <summary>Full DTOs of <paramref name="ids"/>, read through the caller's scope; the exposure from the unscoped world.</summary>
    private async Task<List<ThirdPartyDto>> ComposeAsync(AuditableContext db, IReadOnlyCollection<int> ids, World world,
        bool detail)
    {
        var now = Clock();
        var idList = ids.ToList();

        var parties = await db.ThirdParties.AsNoTracking()
            .Include(t => t.Links)
            .Include(t => t.Subprocessors)
            .Include(t => t.DataLocations)
            .Where(t => idList.Contains(t.Id))
            .ToListAsync();

        var assessments = await db.ThirdPartyAssessments.AsNoTracking()
            .Where(a => idList.Contains(a.ThirdPartyId))
            .ToListAsync();

        var latestLive = assessments
            .Where(a => a.VoidedAt == null)
            .GroupBy(a => a.ThirdPartyId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.RespondedAt ?? a.CreatedAt).ThenByDescending(a => a.Id).First());

        // Every assessment is scored in the detail; only the latest live one in a list.
        var scoredIds = (detail ? assessments.Select(a => a.Id) : latestLive.Values.Select(a => a.Id)).ToList();
        var answers = (await db.ThirdPartyAssessmentAnswers.AsNoTracking()
                .Where(a => scoredIds.Contains(a.AssessmentId))
                .Select(a => new { a.AssessmentId, a.QuestionId, a.Answer, a.PreferredAnswer, a.Weight, a.Critical })
                .ToListAsync())
            .GroupBy(a => a.AssessmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<HecvatAnswerFacts>)g
                .Select(a => new HecvatAnswerFacts(a.QuestionId, a.Answer, a.PreferredAnswer, a.Weight, a.Critical)).ToList());

        var sboms = detail
            ? await db.ThirdPartySboms.AsNoTracking().Where(s => idList.Contains(s.ThirdPartyId)).ToListAsync()
            : [];

        var ownerIds = parties.Where(p => p.OwnerId != null).Select(p => p.OwnerId!.Value).Distinct().ToList();
        var owners = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Value))
            .Select(u => new { u.Value, u.Name }).ToDictionaryAsync(u => u.Value, u => u.Name);

        var linked = parties.SelectMany(p => p.Links).Select(l => l.EntityId).Distinct().ToList();
        var definitions = await db.Entities.AsNoTracking().Where(e => linked.Contains(e.Id))
            .Select(e => new { e.Id, e.DefinitionName }).ToDictionaryAsync(e => e.Id, e => e.DefinitionName);
        var entityNames = (await db.EntitiesProperties.AsNoTracking()
                .Where(p => linked.Contains(p.Entity) && p.Type == RiskChainSchema.NameProperty)
                .Select(p => new { p.Entity, p.Value })
                .ToListAsync())
            .GroupBy(p => p.Entity).ToDictionary(g => g.Key, g => g.First().Value);

        // A registered sub-processor is shown by id only when the reader may see it.
        var named = parties.SelectMany(p => p.Subprocessors).Where(s => s.SubprocessorThirdPartyId != null)
            .Select(s => s.SubprocessorThirdPartyId!.Value).Distinct().ToList();
        var visibleNamed = (await db.ThirdParties.AsNoTracking().Where(t => named.Contains(t.Id)).Select(t => t.Id)
            .ToListAsync()).ToHashSet();

        var result = new List<ThirdPartyDto>();
        foreach (var party in parties)
        {
            var hecvat = latestLive.TryGetValue(party.Id, out var latest)
                ? Score(latest, answers.GetValueOrDefault(latest.Id) ?? [], now)
                : new HecvatResultDto
                {
                    State = HecvatState.NotAssessed, PassThreshold = ThirdPartyLimits.HecvatPassThreshold,
                    Explanation = "No HECVAT is recorded, or every one recorded was voided."
                };

            var exposure = world.Exposures.GetValueOrDefault(party.Id) ?? new SupplierExposure();
            var concentration = Entry(world, party.Id);

            var dto = new ThirdPartyDto
            {
                Id = party.Id, Name = party.Name, LegalName = party.LegalName, TaxId = party.TaxId, Country = party.Country,
                Description = party.Description, Website = party.Website, EntityId = party.EntityId, OwnerId = party.OwnerId,
                OwnerName = party.OwnerId is { } owner ? owners.GetValueOrDefault(owner) : null,
                Status = party.Status, IsCloudProvider = party.IsCloudProvider, IsIdentityProvider = party.IsIdentityProvider,
                ProcessesPersonalData = party.ProcessesPersonalData, SubprocessorsDeclaredAt = party.SubprocessorsDeclaredAt,
                ContractReference = party.ContractReference, ContractStart = party.ContractStart,
                ContractEnd = party.ContractEnd, SlaAvailabilityPercent = party.SlaAvailabilityPercent,
                ContractedRtoMinutes = party.ContractedRtoMinutes, ContractedRpoMinutes = party.ContractedRpoMinutes,
                VulnerabilityFixDays = party.VulnerabilityFixDays, RightToAudit = party.RightToAudit,
                AuditClauseReference = party.AuditClauseReference, ExitPlan = party.ExitPlan,
                ExitPlanReviewedAt = party.ExitPlanReviewedAt, ExitPlanTestedAt = party.ExitPlanTestedAt,
                DataPortability = party.DataPortability, TerminatedAt = party.TerminatedAt, CreatedAt = party.CreatedAt,
                CreatedById = party.CreatedById, UpdatedAt = party.UpdatedAt, UpdatedById = party.UpdatedById,
                Links = party.Links.OrderBy(l => l.Kind).ThenBy(l => l.EntityId).Select(l => new ThirdPartyLinkDto
                {
                    EntityId = l.EntityId, Kind = l.Kind, EntityName = entityNames.GetValueOrDefault(l.EntityId),
                    DefinitionName = definitions.GetValueOrDefault(l.EntityId) ?? string.Empty,
                    Description = l.Description, CreatedAt = l.CreatedAt, CreatedById = l.CreatedById
                }).ToList(),
                Subprocessors = party.Subprocessors.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(s =>
                    new ThirdPartySubprocessorDto
                    {
                        Id = s.Id, Name = s.Name, Service = s.Service, Country = s.Country,
                        ProcessesPersonalData = s.ProcessesPersonalData,
                        SubprocessorThirdPartyId = s.SubprocessorThirdPartyId is { } sub && visibleNamed.Contains(sub)
                            ? sub
                            : null
                    }).ToList(),
                DataLocations = party.DataLocations.OrderBy(l => l.Country, StringComparer.Ordinal).ThenBy(l => l.Purpose)
                    .Select(l => new ThirdPartyDataLocationDto { Id = l.Id, Country = l.Country, Region = l.Region, Purpose = l.Purpose })
                    .ToList(),
                Hecvat = hecvat,
                ContinuityRequirement = exposure.Requirement,
                Concentration = concentration
            };

            if (detail)
            {
                dto.Assessments = assessments.Where(a => a.ThirdPartyId == party.Id)
                    .OrderByDescending(a => a.RespondedAt ?? a.CreatedAt).ThenByDescending(a => a.Id)
                    .Select(a => ToDto(a, answers.GetValueOrDefault(a.Id) ?? [], includeAnswers: false))
                    .ToList();
                dto.Sboms = sboms.Where(s => s.ThirdPartyId == party.Id)
                    .OrderByDescending(s => s.UploadedAt).ThenByDescending(s => s.Id)
                    .Select(s => ToDto(s, includeComponents: false))
                    .ToList();
            }

            dto.Findings = ThirdPartyFindingsEvaluator.Evaluate(new ThirdPartyFacts(
                party.Status, party.ProcessesPersonalData, party.SubprocessorsDeclaredAt, party.DataLocations.Count,
                party.Links.Any(l => l.Kind == ThirdPartyLinkKind.Data), party.RightToAudit, party.ExitPlan,
                party.ExitPlanTestedAt, party.DataPortability, party.ContractedRtoMinutes, party.ContractedRpoMinutes,
                party.VulnerabilityFixDays, party.ContractEnd, party.SlaAvailabilityPercent, hecvat.State,
                concentration.DependentCriticalProcessCount, exposure.Requirement), now);

            result.Add(dto);
        }

        return result;
    }

    private HecvatResultDto Score(ThirdPartyAssessment assessment, IReadOnlyCollection<HecvatAnswerFacts> answers, DateTime now) =>
        HecvatScoring.Score(assessment.ExpectedQuestionCount, answers, assessment.ValidUntil, assessment.VoidedAt is not null, now);

    private ThirdPartyAssessmentDto ToDto(ThirdPartyAssessment a, IReadOnlyCollection<HecvatAnswerFacts> answers,
        bool includeAnswers) => new()
    {
        Id = a.Id, ThirdPartyId = a.ThirdPartyId, Variant = a.Variant, FrameworkVersion = a.FrameworkVersion,
        ExpectedQuestionCount = a.ExpectedQuestionCount, RespondedAt = a.RespondedAt, ValidUntil = a.ValidUntil,
        EvidenceReference = a.EvidenceReference, Notes = a.Notes, CreatedAt = a.CreatedAt, CreatedById = a.CreatedById,
        AnswersUpdatedAt = a.AnswersUpdatedAt, AnswersUpdatedById = a.AnswersUpdatedById, VoidedAt = a.VoidedAt,
        VoidReason = a.VoidReason,
        Result = Score(a, answers, Clock()),
        Answers = includeAnswers
            ? a.Answers.OrderBy(x => x.QuestionId, StringComparer.Ordinal).Select(x => new HecvatAnswerDto
            {
                QuestionId = x.QuestionId, Answer = x.Answer, PreferredAnswer = x.PreferredAnswer, Weight = x.Weight,
                Critical = x.Critical, Notes = x.Notes
            }).ToList()
            : []
    };

    private static HecvatAnswerFacts Facts(ThirdPartyAssessmentAnswer a) =>
        new(a.QuestionId, a.Answer, a.PreferredAnswer, a.Weight, a.Critical);

    private static ThirdPartySbomDto ToDto(ThirdPartySbom s, bool includeComponents) => new()
    {
        Id = s.Id, ThirdPartyId = s.ThirdPartyId, ComponentName = s.ComponentName, ComponentVersion = s.ComponentVersion,
        Format = s.Format, SpecVersion = s.SpecVersion, SerialNumber = s.SerialNumber, DocumentSha256 = s.DocumentSha256,
        DocumentSizeBytes = s.DocumentSizeBytes, FileName = s.FileName, ComponentCount = s.ComponentCount,
        UploadedAt = s.UploadedAt, UploadedById = s.UploadedById,
        Components = includeComponents
            ? s.Components.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Version, StringComparer.Ordinal)
                .Select(c => new SbomComponentDto { Name = c.Name, Version = c.Version, Purl = c.Purl, License = c.License })
                .ToList()
            : []
    };

    // --- validation and guards ---------------------------------------------------------------------------------------

    /// <summary>Validates and normalizes a request; the copy returned carries the trimmed, upper-cased, UTC values.</summary>
    private ThirdPartyRequest Validate(ThirdPartyRequest request)
    {
        var now = Clock();

        var valid = new ThirdPartyRequest
        {
            Name = Required(request.Name, ThirdPartyLimits.MaxNameLength, nameof(ThirdPartyRequest.Name),
                "The third party needs a name."),
            LegalName = Optional(request.LegalName, ThirdPartyLimits.MaxLegalNameLength, nameof(ThirdPartyRequest.LegalName)),
            TaxId = Optional(request.TaxId, ThirdPartyLimits.MaxTaxIdLength, nameof(ThirdPartyRequest.TaxId)),
            Country = Country(request.Country, nameof(ThirdPartyRequest.Country)),
            Description = Optional(request.Description, ThirdPartyLimits.MaxDescriptionLength,
                nameof(ThirdPartyRequest.Description)),
            Website = Optional(request.Website, ThirdPartyLimits.MaxWebsiteLength, nameof(ThirdPartyRequest.Website)),
            EntityId = request.EntityId,
            OwnerId = request.OwnerId,
            Status = request.Status,
            IsCloudProvider = request.IsCloudProvider,
            IsIdentityProvider = request.IsIdentityProvider,
            ProcessesPersonalData = request.ProcessesPersonalData,
            ContractReference = Optional(request.ContractReference, ThirdPartyLimits.MaxContractReferenceLength,
                nameof(ThirdPartyRequest.ContractReference)),
            ContractStart = Utc(request.ContractStart),
            ContractEnd = Utc(request.ContractEnd),
            SlaAvailabilityPercent = request.SlaAvailabilityPercent,
            ContractedRtoMinutes = request.ContractedRtoMinutes,
            ContractedRpoMinutes = request.ContractedRpoMinutes,
            VulnerabilityFixDays = request.VulnerabilityFixDays,
            RightToAudit = request.RightToAudit,
            AuditClauseReference = Optional(request.AuditClauseReference, ThirdPartyLimits.MaxAuditClauseReferenceLength,
                nameof(ThirdPartyRequest.AuditClauseReference)),
            ExitPlan = Optional(request.ExitPlan, ThirdPartyLimits.MaxExitPlanLength, nameof(ThirdPartyRequest.ExitPlan)),
            ExitPlanReviewedAt = Utc(request.ExitPlanReviewedAt),
            ExitPlanTestedAt = Utc(request.ExitPlanTestedAt),
            DataPortability = Optional(request.DataPortability, ThirdPartyLimits.MaxDataPortabilityLength,
                nameof(ThirdPartyRequest.DataPortability))
        };

        // Stored only if a client may open it: an absolute http(s) URL, no whitespace or control characters (NR-2026-023).
        if (valid.Website is { } website && !ExternalUrlPolicy.IsOpenable(website))
            throw new InvalidParameterException(nameof(ThirdPartyRequest.Website), "The website is an absolute http or https URL.");

        if (valid.EntityId is <= 0)
            throw new InvalidParameterException(nameof(ThirdPartyRequest.EntityId), "The entity is an entity id.");
        if (valid.OwnerId is <= 0)
            throw new InvalidParameterException(nameof(ThirdPartyRequest.OwnerId), "The owner is a user id.");

        if (valid.Status is { } status && !Enum.IsDefined(status))
            throw new InvalidParameterException(nameof(ThirdPartyRequest.Status),
                "The status is prospective (1), active (2), exiting (3) or terminated (4).");

        if (valid.ContractStart is { } start && valid.ContractEnd is { } end && end < start)
            throw new InvalidParameterException(nameof(ThirdPartyRequest.ContractEnd), "The contract cannot end before it starts.");

        if (valid.SlaAvailabilityPercent is { } sla && (sla <= 0 || sla > 100 || decimal.Round(sla, 3) != sla))
            throw new InvalidParameterException(nameof(ThirdPartyRequest.SlaAvailabilityPercent),
                "The contracted availability is a percentage above 0 and at most 100, with up to three decimals.");

        Duration(valid.ContractedRtoMinutes, nameof(ThirdPartyRequest.ContractedRtoMinutes));
        Duration(valid.ContractedRpoMinutes, nameof(ThirdPartyRequest.ContractedRpoMinutes));

        if (valid.VulnerabilityFixDays is < 0 or > ThirdPartyLimits.MaxVulnerabilityFixDays)
            throw new InvalidParameterException(nameof(ThirdPartyRequest.VulnerabilityFixDays),
                $"The deadline is 0 to {ThirdPartyLimits.MaxVulnerabilityFixDays} days.");

        if (valid.ExitPlanReviewedAt > now)
            throw new InvalidParameterException(nameof(ThirdPartyRequest.ExitPlanReviewedAt), "A review cannot be in the future.");
        if (valid.ExitPlanTestedAt > now)
            throw new InvalidParameterException(nameof(ThirdPartyRequest.ExitPlanTestedAt), "A test cannot be in the future.");

        return valid;
    }

    private static void Apply(ThirdParty party, ThirdPartyRequest valid, ThirdPartyStatus status, DateTime now)
    {
        party.Name = valid.Name!;
        party.LegalName = valid.LegalName;
        party.TaxId = valid.TaxId;
        party.Country = valid.Country;
        party.Description = valid.Description;
        party.Website = valid.Website;
        party.EntityId = valid.EntityId;
        party.OwnerId = valid.OwnerId;
        party.IsCloudProvider = valid.IsCloudProvider;
        party.IsIdentityProvider = valid.IsIdentityProvider;
        party.ProcessesPersonalData = valid.ProcessesPersonalData;
        party.ContractReference = valid.ContractReference;
        party.ContractStart = valid.ContractStart;
        party.ContractEnd = valid.ContractEnd;
        party.SlaAvailabilityPercent = valid.SlaAvailabilityPercent;
        party.ContractedRtoMinutes = valid.ContractedRtoMinutes;
        party.ContractedRpoMinutes = valid.ContractedRpoMinutes;
        party.VulnerabilityFixDays = valid.VulnerabilityFixDays;
        party.RightToAudit = valid.RightToAudit;
        party.AuditClauseReference = valid.AuditClauseReference;
        party.ExitPlan = valid.ExitPlan;
        party.ExitPlanReviewedAt = valid.ExitPlanReviewedAt;
        party.ExitPlanTestedAt = valid.ExitPlanTestedAt;
        party.DataPortability = valid.DataPortability;

        // Terminated exactly when the status says so (ck_third_parties_terminated); the date of the first termination is kept.
        if (status == ThirdPartyStatus.Terminated) party.TerminatedAt ??= now;
        else party.TerminatedAt = null;
        party.Status = status;
    }

    private async Task RequireUniqueNameAsync(string name, int? exceptId)
    {
        // Organization-wide and unscoped: two rows for one supplier would split its concentration (S51 D5).
        await using var system = SystemContext();
        var lowered = name.ToLowerInvariant();
        if (await system.ThirdParties.AnyAsync(t => t.Name.ToLower() == lowered && (exceptId == null || t.Id != exceptId)))
            throw new DataAlreadyExistsException("netrisk", "third_parties", name,
                $"A third party named '{name}' is already registered.");
    }

    private static async Task SaveOrConflictAsync(AuditableContext db, string name)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ContinuityService.Mentions(ex, "uq_third_parties_name"))
        {
            throw new DataAlreadyExistsException("netrisk", "third_parties", name,
                $"A third party named '{name}' is already registered.");
        }
    }

    private static async Task RequireEntityAsync(AuditableContext db, int? entityId)
    {
        if (entityId is { } entity && !await db.Entities.AnyAsync(e => e.Id == entity))
            throw new DataNotFoundException("entities", entity.ToString(Invariant));
    }

    private static async Task RequireOwnerAsync(AuditableContext db, int? ownerId)
    {
        if (ownerId is not { } id) return;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Value == id)
                   ?? throw new DataNotFoundException("user", id.ToString(Invariant));

        if (user.Enabled != true)
            throw new InvalidParameterException(nameof(ThirdPartyRequest.OwnerId),
                "A disabled account cannot own a supplier relationship.");

        // The third line assures the first; it does not own what it audits (S50 §4.7, S51 §4.1).
        await ThirdLineGuard.EnsureNotThirdLineAsync(db, id, "own a supplier relationship");
    }

    /// <summary>
    /// A write on a third party — on the record or on anything it declares — needs the third party's own entity in the
    /// caller's scope (S51 §4.10). The context's write guard sees only <see cref="ThirdParty"/> rows added or modified, and
    /// only where they go: a link, an answer, an SBOM or a deletion would pass it. The <c>MonitoringService</c> precedent.
    /// </summary>
    private static void RequireWritable(AuditableContext db, ThirdParty party)
    {
        if (!db.EntityScope.Allows(party.EntityId))
            throw new DAL.Exceptions.EntityScopeViolationException(nameof(ThirdParty), party.EntityId,
                db.EntityScope.ToString());
    }

    private static async Task<ThirdParty> RequireWritablePartyAsync(AuditableContext db, int thirdPartyId)
    {
        var party = await db.ThirdParties.AsNoTracking().FirstOrDefaultAsync(t => t.Id == thirdPartyId)
                    ?? throw NotFound(thirdPartyId);
        RequireWritable(db, party);
        return party;
    }

    private static async Task RequireVisibleAsync(AuditableContext db, int thirdPartyId)
    {
        if (!await db.ThirdParties.AsNoTracking().AnyAsync(t => t.Id == thirdPartyId)) throw NotFound(thirdPartyId);
    }

    private static void RefuseTerminated(ThirdParty party)
    {
        if (party.Status == ThirdPartyStatus.Terminated)
            throw new RuleBrokenException(
                $"The relationship with '{party.Name}' was terminated: it takes no new link, declaration, HECVAT or SBOM. " +
                "Set it active again first if it resumed.", TerminatedRule);
    }

    private static DataNotFoundException NotFound(int thirdPartyId) =>
        new("third_parties", thirdPartyId.ToString(Invariant));

    private static void Duration(int? minutes, string parameter)
    {
        if (minutes is < 0 or > ThirdPartyLimits.MaxDurationMinutes)
            throw new InvalidParameterException(parameter,
                $"A contracted objective is 0 to {ThirdPartyLimits.MaxDurationMinutes} minutes (365 days).");
    }

    private static string? Country(string? code, string parameter)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var upper = code.Trim().ToUpperInvariant();
        if (!CountryCode.IsMatch(upper))
            throw new InvalidParameterException(parameter, $"'{Shorten(code)}' is not an ISO 3166-1 alpha-2 country code.");

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

    /// <summary>An echo of caller input in a message, never longer than a few words.</summary>
    private static string Shorten(string? text) =>
        text is null ? string.Empty : SbomParser.Clean(text, 30) ?? string.Empty;

    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { } v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
    };

    private AuditableContext SystemContext() => DalService.GetContext(withIdentity: false, bypassEntityScope: true);
}

/// <summary>
/// What refers to a third party (S51 §4.9) — <b>the registry to extend when a new table names a third party</b>, as
/// <c>SecretVaultService.CountReferencesAsync</c> is for vault connections. Stage 9.10 adds no credential column, so that
/// registry is unchanged; Stage 9.11 adds here whatever of its catalogue names a supplier, and Stage 9.12 the AI models that
/// name one as their vendor. A use missed here does not corrupt anything: the foreign key's <c>RESTRICT</c> still refuses
/// the delete, as a 500 instead of a sentence.
/// </summary>
public static class ThirdPartyReferences
{
    /// <param name="LegalRequirements">
    /// Stage 9.11 (S52 §4.9): the catalogued contracts that name the third party as their counterparty — the extension
    /// S51 §4.9 asked for when the catalogue names a supplier.
    /// </param>
    /// <param name="AiModels">
    /// Stage 9.12 (S53 §4.1): the inventoried AI models that name the third party as their vendor — retired ones included,
    /// because the inventory keeps a retired model as evidence.
    /// </param>
    public sealed record Counts(int Links, int NamedAsSubprocessor, int Assessments, int Sboms, int LegalRequirements,
        int AiModels)
    {
        public int Total => Links + NamedAsSubprocessor + Assessments + Sboms + LegalRequirements + AiModels;

        public string Describe()
        {
            var parts = new List<string>();
            if (Links > 0) parts.Add($"{Links} link(s) to services, processes or data records");
            if (NamedAsSubprocessor > 0) parts.Add($"named as a sub-processor by {NamedAsSubprocessor} supplier(s)");
            if (Assessments > 0) parts.Add($"{Assessments} HECVAT assessment(s)");
            if (Sboms > 0) parts.Add($"{Sboms} SBOM(s)");
            if (LegalRequirements > 0) parts.Add($"named as the counterparty of {LegalRequirements} catalogued contract(s)");
            if (AiModels > 0) parts.Add($"named as the vendor of {AiModels} inventoried AI model(s)");
            return string.Join(", ", parts);
        }
    }

    /// <summary>Counts every use; pass an unscoped context, so a use the caller cannot see still counts.</summary>
    public static async Task<Counts> CountAsync(AuditableContext db, int thirdPartyId)
    {
        ArgumentNullException.ThrowIfNull(db);

        return new Counts(
            await db.ThirdPartyLinks.CountAsync(l => l.ThirdPartyId == thirdPartyId),
            await db.ThirdPartySubprocessors.CountAsync(s => s.SubprocessorThirdPartyId == thirdPartyId),
            await db.ThirdPartyAssessments.CountAsync(a => a.ThirdPartyId == thirdPartyId),
            await db.ThirdPartySboms.CountAsync(s => s.ThirdPartyId == thirdPartyId),
            await db.LegalRequirements.CountAsync(r => r.ThirdPartyId == thirdPartyId),
            await db.AiModels.CountAsync(m => m.ThirdPartyId == thirdPartyId));
    }
}
