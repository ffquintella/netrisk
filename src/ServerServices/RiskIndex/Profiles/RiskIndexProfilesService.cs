using System.Globalization;
using System.Text.Json;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Assessments;
using Model.RiskIndex;
using ServerServices.Interfaces;
using ServerServices.RiskIndex.Engine;
using ServerServices.RiskIndex.Read;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace ServerServices.RiskIndex.Profiles;

/// <summary>
/// M53, T250 (S39 §12.4, §13.1–§13.2; S40 §6.2–§6.4, §8.4, §11.4). Every transition is a field-audited save of the profile
/// (<c>GovernanceAuditInterceptor</c>) and increments <c>revision</c>, the EF concurrency token, so two racing transitions
/// cannot both land. The approval re-reads the approver's permissions from the database and applies the segregation of
/// duties itself; the policy attribute in front of it is necessary, never sufficient.
/// </summary>
public sealed partial class RiskIndexProfilesService(
    IDalService dalService,
    IPermissionsService permissionsService,
    IAuditTrailService auditTrail,
    IRiskIndexReadCache cache,
    ILogger logger) : IRiskIndexProfilesService
{
    /// <summary>A rebaseline proposal explains itself (S40 §6.3).</summary>
    public const int RebaselineJustificationMinLength = 40;

    public const int MaxNameLength = 120;
    public const int MaxReferenceLength = 200;
    public const int MaxTextLength = 4000;
    public const int MaxHistory = 5000;

    // ------------------------------------------------------------------------------------------------------------------
    // Reads of the configuration: any risk_index_view holder, any scope (S40 §8.4)
    // ------------------------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<RiskIndexProfileSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        var profiles = await db.RiskIndexProfiles.AsNoTracking().OrderByDescending(p => p.Version).ToListAsync(ct);
        var names = await UserNamesAsync(db, profiles.SelectMany(p => new[] { p.CreatedById, p.ApprovedById }), ct);
        return profiles.Select(p => RiskIndexReadService.Summary(p, Name(names, p.CreatedById), NameOrNull(names, p.ApprovedById)))
            .ToList();
    }

    public async Task<RiskIndexProfileDto> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        return await DtoAsync(db, await FindAsync(db, id, tracked: false, ct), ct);
    }

    public async Task<RiskIndexProfileDto?> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        var active = await ActiveAsync(db, DateTime.UtcNow, ct);
        return active is null ? null : await DtoAsync(db, active, ct);
    }

    public async Task<RiskIndexProfileDiffDto> DiffAsync(int a, int b, CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        var left = await FindAsync(db, a, tracked: false, ct);
        var right = await FindAsync(db, b, tracked: false, ct);
        return new RiskIndexProfileDiffDto(a, b, RiskIndexProfileRules.Diff(Parameters(left), Parameters(right)));
    }

    public RiskIndexParameterSchemaDto GetSchema() => RiskIndexProfileRules.Schema();

    public IReadOnlyList<RiskIndexPresetDto> GetPresets() => RiskIndexPresets.List();

    // ------------------------------------------------------------------------------------------------------------------
    // Draft (risk_index_configure)
    // ------------------------------------------------------------------------------------------------------------------

    public async Task<RiskIndexProfileDto> CreateAsync(RiskIndexProfileCreateRequest request, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        Require(caller, RiskIndexPermissions.Configure);
        var name = Name(request.Name);

        await using var db = dalService.GetContext();
        if (await db.RiskIndexProfiles.AnyAsync(p => p.Status == RiskIndexProfileStatus.Draft ||
                                                     p.Status == RiskIndexProfileStatus.PendingApproval, ct))
            throw RiskIndexException.Conflict(RiskIndexErrors.OpenDraftExists,
                "A draft or a profile awaiting approval already exists; finish or delete it first.");

        var everApproved = await db.RiskIndexProfiles.AnyAsync(p => p.Status == RiskIndexProfileStatus.Active ||
                                                                     p.Status == RiskIndexProfileStatus.Retired, ct);
        if (request.Preset is { } key)
        {
            if (!RiskIndexPresets.IsKnown(key))
                throw RiskIndexException.BadRequest(RiskIndexErrors.UnknownPreset, "The preset is not one of S39 §12.3.");
            if (!RiskIndexPresets.IsActivatable(key))
                throw RiskIndexException.BadRequest(RiskIndexErrors.PresetNotActivatable,
                    "\"Pure mean\" exists only to show the dilution in the preview; it cannot be a profile.");
        }

        RiskIndexParameters parameters;
        var mode = RiskIndexMode.Interim;
        var preset = request.Preset;
        if (request.FromProfileId is { } from)
        {
            var source = await FindAsync(db, from, tracked: false, ct);
            parameters = Parameters(source);
            mode = source.Mode;
            preset ??= source.Preset;
        }
        else if (request.Preset is { } chosen) parameters = RiskIndexPresets.Parameters(chosen);
        else if (!everApproved)
            throw RiskIndexException.BadRequest(RiskIndexErrors.PresetRequiredForFirstProfile,
                "The first profile starts from a preset (S39 §12.3).");
        else
        {
            var baseline = (await BaselineAsync(db, DateTime.UtcNow, ct))!;
            parameters = Parameters(baseline);
            mode = baseline.Mode;
            preset = baseline.Preset;
        }

        // tipos_avaliaveis defaults to "the types with a Submitted run", which only the organization knows (S39 §12.2).
        if (parameters.AssessableTypes.Count == 0)
            parameters = parameters with { AssessableTypes = await AssessableTypesAsync(db, ct) };

        var version = (await db.RiskIndexProfiles.MaxAsync(p => (int?)p.Version, ct) ?? 0) + 1;
        var profile = new RiskIndexProfile
        {
            Version = version, Name = name, Preset = preset, Mode = mode, Status = RiskIndexProfileStatus.Draft,
            Parameters = RiskIndexParametersJson.Serialize(parameters), CreatedAt = DateTime.UtcNow, CreatedById = caller.UserId,
            Revision = 0
        };
        db.RiskIndexProfiles.Add(profile);
        await RiskIndexProfileAuthors.TrackAsync(db, profile, caller.UserId, profile.CreatedAt, ct);
        await db.SaveChangesAsync(ct);
        logger.Information("Risk index profile v{Version} drafted by user {UserId}", version, caller.UserId);
        return await DtoAsync(db, profile, ct);
    }

    public async Task<RiskIndexProfileDto> UpdateAsync(int id, RiskIndexProfileUpdateRequest request, int expectedRevision,
        RiskIndexCaller caller, CancellationToken ct = default)
    {
        Require(caller, RiskIndexPermissions.Configure);
        var name = Name(request.Name);
        if (request.Parameters is null)
            throw RiskIndexException.BadRequest(RiskIndexErrors.ParameterOutOfRange, "The parameters are required.");
        string json;
        try
        {
            json = RiskIndexParametersJson.Serialize(request.Parameters);
            RiskIndexParametersJson.Deserialize(json);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            // Structural only (S40 §8.4): a value no JSON number can hold. Ranges are the validation's, not the save's.
            throw RiskIndexException.BadRequest(RiskIndexErrors.ParameterOutOfRange, "The parameters are not a valid document.");
        }

        if (!Enum.IsDefined(request.Mode))
            throw RiskIndexException.BadRequest(RiskIndexErrors.ParameterOutOfRange, "The mode is interim (1) or full (2).");

        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: true, ct);
        if (profile.Status != RiskIndexProfileStatus.Draft)
            throw RiskIndexException.Conflict(RiskIndexErrors.InvalidTransition, "Only a draft is edited.");
        if (profile.Revision != expectedRevision) throw Stale(profile.Revision);

        profile.Name = name;
        profile.Mode = request.Mode;
        profile.Parameters = json;
        Touch(db, profile, expectedRevision);

        // The editor is recorded in this same save: the approval's segregation of duties reads it (S40 §6.4).
        await RiskIndexProfileAuthors.TrackAsync(db, profile, caller.UserId, profile.UpdatedAt!.Value, ct);
        await SaveAsync(db, () => Stale(null), ct);
        return await DtoAsync(db, profile, ct);
    }

    public async Task DeleteAsync(int id, RiskIndexCaller caller, CancellationToken ct = default)
    {
        Require(caller, RiskIndexPermissions.Configure);
        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: true, ct);
        if (profile.Status != RiskIndexProfileStatus.Draft)
            throw RiskIndexException.Conflict(RiskIndexErrors.InvalidTransition, "Only a draft is deleted.");

        // The previews go with the draft (FK CASCADE in the database; removed here too, so every provider agrees).
        db.RiskIndexPreviewRequests.RemoveRange(await db.RiskIndexPreviewRequests.Where(r => r.ProfileId == id).ToListAsync(ct));
        db.RiskIndexProfileEditors.RemoveRange(await db.RiskIndexProfileEditors.Where(e => e.ProfileId == id).ToListAsync(ct));
        db.RiskIndexProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);
    }

    public async Task<RiskIndexValidationResultDto> ValidateAsync(int id, CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: false, ct);
        return await ValidateAsync(db, profile, ct);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Preview and submission
    // ------------------------------------------------------------------------------------------------------------------

    public async Task<RiskIndexPreviewStatusDto> RequestPreviewAsync(int id, RiskIndexCaller caller, CancellationToken ct = default)
    {
        Require(caller, RiskIndexPermissions.Configure);
        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: false, ct);
        if (profile.Status != RiskIndexProfileStatus.Draft)
            throw RiskIndexException.Conflict(RiskIndexErrors.InvalidTransition, "Only a draft is previewed.");

        // One preview per draft at a time (S40 §11.8): the job is the only consumer of the queue.
        if (await db.RiskIndexPreviewRequests.AnyAsync(r => r.ProfileId == id &&
                                                            (r.Status == RiskIndexPreviewStatus.Queued ||
                                                             r.Status == RiskIndexPreviewStatus.Running), ct))
            throw RiskIndexException.Conflict(RiskIndexErrors.PreviewInProgress, "A preview of this draft is already queued or running.");

        var request = new RiskIndexPreviewRequest
        {
            ProfileId = id, ParametersHash = RiskIndexPreviewService.ParametersHash(profile.Mode, profile.Parameters),
            Status = RiskIndexPreviewStatus.Queued, RequestedById = caller.UserId, CreatedAt = DateTime.UtcNow
        };
        db.RiskIndexPreviewRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return new RiskIndexPreviewStatusDto(request.Id, RiskIndexPreviewState.Queued, 0, request.ParametersHash, null, null);
    }

    public async Task<RiskIndexPreviewDto> GetPreviewAsync(int id, RiskIndexCaller caller, CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: false, ct);
        await EnsureMayReadValuesAsync(db, profile, caller, ct);

        RiskIndexPreviewDto? report;
        RiskIndexPreviewStatusDto status;
        if (profile.Status == RiskIndexProfileStatus.Draft)
        {
            var request = await db.RiskIndexPreviewRequests.AsNoTracking().Where(r => r.ProfileId == id)
                              .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct)
                          ?? throw RiskIndexException.NotFound(RiskIndexErrors.PreviewMissing, "This draft has no preview.");
            var stale = request.Status == RiskIndexPreviewStatus.Completed &&
                        request.ParametersHash != RiskIndexPreviewService.ParametersHash(profile.Mode, profile.Parameters);
            status = new RiskIndexPreviewStatusDto(request.Id,
                stale ? RiskIndexPreviewState.Stale : (RiskIndexPreviewState)(int)request.Status, request.Progress,
                request.ParametersHash, request.FinishedAt, request.ErrorMessage);
            report = Deserialize(request.Report);
        }
        else
        {
            report = Deserialize(profile.SensitivityReport)
                     ?? throw RiskIndexException.NotFound(RiskIndexErrors.PreviewMissing, "This profile has no attached preview.");
            status = report.Status;
        }

        if (report is null) return new RiskIndexPreviewDto(status, [], null, 0, [], [], 0, 0, [], [], false);

        // The objects are named for this reader: the report stores none of their labels (S40 §6.3, §11.3).
        var labels = await RiskIndexLabels.ResolveAsync(db,
            report.TopMovers.Select(m => new IcrObjectKey(m.Object.Object.Kind, m.Object.Object.Ref)), caller, ct);
        return report with
        {
            Status = status,
            TopMovers = report.TopMovers
                .Select(m => m with { Object = labels[new IcrObjectKey(m.Object.Object.Kind, m.Object.Object.Ref)] }).ToList()
        };
    }

    public async Task<RiskIndexProfileDto> SubmitAsync(int id, RiskIndexSubmitRequest request, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        Require(caller, RiskIndexPermissions.Configure);
        var justification = request.Justification?.Trim() ?? "";
        if (justification.Length == 0)
            throw RiskIndexException.BadRequest(RiskIndexErrors.JustificationRequired, "The submission needs a justification.");
        if (justification.Length > MaxTextLength)
            throw RiskIndexException.BadRequest(RiskIndexErrors.JustificationRequired, "The justification is too long.");

        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: true, ct);
        if (profile.Status != RiskIndexProfileStatus.Draft)
            throw RiskIndexException.Conflict(RiskIndexErrors.InvalidTransition, "Only a draft is submitted.");

        var validation = await ValidateAsync(db, profile, ct);
        if (!validation.IsValid)
            throw RiskIndexException.BadRequest(RiskIndexErrors.ProfileInvalid, "The draft does not pass the S39 §12.4 rules.",
                new Dictionary<string, object?> { ["errors"] = validation.Errors });
        if (validation.RequiresRebaseline && justification.Length < RebaselineJustificationMinLength)
            throw RiskIndexException.BadRequest(RiskIndexErrors.RebaselineReasonRequired,
                $"A rebaseline proposal explains itself in at least {RebaselineJustificationMinLength} characters.");

        // The preview is attached (S39 §13.2): the completed one computed for exactly these parameters.
        var hash = RiskIndexPreviewService.ParametersHash(profile.Mode, profile.Parameters);
        var completed = await db.RiskIndexPreviewRequests.AsNoTracking()
            .Where(r => r.ProfileId == id && r.Status == RiskIndexPreviewStatus.Completed)
            .OrderByDescending(r => r.Id).ToListAsync(ct);
        if (completed.Count == 0)
            throw RiskIndexException.Conflict(RiskIndexErrors.PreviewMissing, "Run the sensitivity preview before submitting.");
        var current = completed.FirstOrDefault(r => r.ParametersHash == hash)
                      ?? throw RiskIndexException.Conflict(RiskIndexErrors.PreviewStale,
                          "The parameters changed since the last preview; run it again for these parameters.");

        var report = Deserialize(current.Report);
        if (report is not null)
            report = report with
            {
                Status = new RiskIndexPreviewStatusDto(current.Id, RiskIndexPreviewState.Completed, 100, current.ParametersHash,
                    current.FinishedAt, null)
            };

        var revision = profile.Revision;
        profile.Status = RiskIndexProfileStatus.PendingApproval;
        profile.SubmittedAt = DateTime.UtcNow;
        profile.SubmittedById = caller.UserId;
        profile.Justification = justification;
        profile.SensitivityReport = report is null ? current.Report : JsonSerializer.Serialize(report, RiskIndexPreviewService.ReportJson);
        Touch(db, profile, revision);
        await SaveAsync(db, Transition, ct);
        logger.Information("Risk index profile v{Version} submitted by user {UserId}", profile.Version, caller.UserId);
        return await DtoAsync(db, profile, ct);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------------------------

    internal static void Require(RiskIndexCaller caller, string permission)
    {
        if (!caller.Has(permission))
            throw RiskIndexException.Forbidden("not_permitted", $"This action requires {permission}.");
    }

    private static string Name(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
            throw RiskIndexException.BadRequest(RiskIndexErrors.NameRequired, $"The name is 1 to {MaxNameLength} characters.");
        return trimmed;
    }

    internal static RiskIndexParameters Parameters(RiskIndexProfile profile) => RiskIndexParametersJson.Deserialize(profile.Parameters);

    private static async Task<RiskIndexProfile> FindAsync(NRDbContext db, int id, bool tracked, CancellationToken ct)
    {
        var query = tracked ? db.RiskIndexProfiles : db.RiskIndexProfiles.AsNoTracking();
        return await query.FirstOrDefaultAsync(p => p.Id == id, ct)
               ?? throw RiskIndexException.NotFound(RiskIndexErrors.ProfileNotFound, "The profile does not exist.");
    }

    /// <summary>The concurrency check is the client's revision; the save increments it (S40 §6.2.1).</summary>
    private static void Touch(NRDbContext db, RiskIndexProfile profile, int expectedRevision)
    {
        db.Entry(profile).Property(p => p.Revision).OriginalValue = expectedRevision;
        profile.Revision = expectedRevision + 1;
        profile.UpdatedAt = DateTime.UtcNow;
    }

    private static async Task SaveAsync(NRDbContext db, Func<RiskIndexException> onConflict, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw onConflict();
        }
    }

    private static RiskIndexException Stale(int? current) =>
        RiskIndexException.Conflict(RiskIndexErrors.StaleDraft, "The draft changed since you read it; reload it.",
            current is null ? null : new Dictionary<string, object?> { ["revision"] = current });

    private static RiskIndexException Transition() =>
        RiskIndexException.Conflict(RiskIndexErrors.InvalidTransition, "The profile changed state meanwhile; reload it.");

    private static RiskIndexPreviewDto? Deserialize(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<RiskIndexPreviewDto>(json, RiskIndexPreviewService.ReportJson);

    /// <summary>The profile in force now, else the most recently approved one: the baseline of the pace rules.</summary>
    internal static async Task<RiskIndexProfile?> BaselineAsync(NRDbContext db, DateTime now, CancellationToken ct) =>
        await ActiveAsync(db, now, ct)
        ?? await db.RiskIndexProfiles.AsNoTracking()
            .Where(p => p.Status == RiskIndexProfileStatus.Active || p.Status == RiskIndexProfileStatus.Retired)
            .OrderByDescending(p => p.ApprovedAt).ThenByDescending(p => p.Version).FirstOrDefaultAsync(ct);

    /// <summary>The profile in force at the next snapshot, else the one in force now.</summary>
    private static async Task<RiskIndexProfile?> ActiveAsync(NRDbContext db, DateTime now, CancellationToken ct) =>
        await db.RiskIndexProfiles.AsNoTracking()
            .Where(p => p.Status == RiskIndexProfileStatus.Active && (p.RetiredAt == null || p.RetiredAt > now))
            .OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Version).FirstOrDefaultAsync(ct);

    /// <summary>
    /// The quarter's baseline (S39 §12.4): the profile in force on the first day of the current UTC calendar quarter, or,
    /// if none was, the first profile approved in the quarter — so two approvals in one quarter cannot move ω by ±0.10.
    /// </summary>
    internal static async Task<RiskIndexProfile?> TrimesterBaselineAsync(NRDbContext db, DateTime now, CancellationToken ct)
    {
        var quarterStart = new DateTime(now.Year, (now.Month - 1) / 3 * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var atStart = quarterStart + RiskIndexSnapshotService.SnapshotTimeOfDay;
        return await RiskIndexSnapshotService.ProfileInForceAsync(db, atStart, ct)
               ?? await db.RiskIndexProfiles.AsNoTracking()
                   .Where(p => (p.Status == RiskIndexProfileStatus.Active || p.Status == RiskIndexProfileStatus.Retired) &&
                               p.ApprovedAt >= quarterStart)
                   .OrderBy(p => p.ApprovedAt).ThenBy(p => p.Version).FirstOrDefaultAsync(ct);
    }

    internal static async Task<RiskIndexValidationResultDto> ValidateAsync(NRDbContext db, RiskIndexProfile profile,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var baseline = await BaselineAsync(db, now, ct);
        if (baseline?.Id == profile.Id) baseline = null;
        var quarter = await TrimesterBaselineAsync(db, now, ct);
        if (quarter?.Id == profile.Id) quarter = null;

        RiskIndexParameters draft;
        try
        {
            draft = Parameters(profile);
        }
        catch (JsonException)
        {
            return new RiskIndexValidationResultDto(false,
                [new RiskIndexValidationIssueDto(RiskIndexErrors.ParameterOutOfRange, null, new Dictionary<string, string>())],
                [], [], false, [], true, null);
        }

        // An Active or Retired profile is validated against the one it was proposed beside, never against itself.
        return RiskIndexProfileRules.Validate(profile.Mode, draft, baseline?.Mode, baseline is null ? null : Parameters(baseline),
            quarter is null ? null : Parameters(quarter), profile.Preset, baseline?.Preset, quarter?.Id);
    }

    private static async Task<List<string>> AssessableTypesAsync(NRDbContext db, CancellationToken ct)
    {
        var entities = await db.AssessmentRuns.AsNoTracking()
            .Where(r => r.Status == (int)AssessmentStatus.Submitted && r.HostId == null)
            .Select(r => r.EntityId).Distinct().ToListAsync(ct);
        return await db.Entities.AsNoTracking().Where(e => entities.Contains(e.Id)).Select(e => e.DefinitionName).Distinct()
            .OrderBy(d => d).ToListAsync(ct);
    }

    private static async Task<Dictionary<int, string>> UserNamesAsync(NRDbContext db, IEnumerable<int?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        return list.Count == 0 ? [] : await db.Users.AsNoTracking().Where(u => list.Contains(u.Value))
            .ToDictionaryAsync(u => u.Value, u => u.Name, ct);
    }

    private static string Name(IReadOnlyDictionary<int, string> names, int? id) => NameOrNull(names, id) ?? "";

    private static string? NameOrNull(IReadOnlyDictionary<int, string> names, int? id) =>
        id is { } i ? names.GetValueOrDefault(i) ?? "#" + i.ToString(CultureInfo.InvariantCulture) : null;

    private static async Task<RiskIndexProfileDto> DtoAsync(NRDbContext db, RiskIndexProfile p, CancellationToken ct)
    {
        var names = await UserNamesAsync(db, [p.CreatedById, p.ApprovedById, p.RetiredById], ct);
        RiskIndexParameters parameters;
        try
        {
            parameters = Parameters(p);
        }
        catch (JsonException)
        {
            parameters = new RiskIndexParameters();
        }

        return new RiskIndexProfileDto(p.Id, p.Version, p.Name, p.Preset, p.Mode, p.Status, parameters, p.Justification,
            p.CommitteeDecisionRef, p.BoardApprovalRef, p.IsRebaseline, p.RejectedReason, Name(names, p.CreatedById), p.CreatedAt,
            p.SubmittedAt, NameOrNull(names, p.ApprovedById), p.ApprovedAt, p.ApprovalGrantSource, p.EffectiveFrom, p.RetiredAt,
            p.RetireJustification, p.RetireDecisionRef, NameOrNull(names, p.RetiredById), p.Revision,
            !string.IsNullOrEmpty(p.SensitivityReport));
    }
}
