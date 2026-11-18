using System.Text.Json;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Integrations;
using ServerServices.Integrations.TrendMicro;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace ServerServices.Integrations.EntityMappings;

/// <summary>The vendor → entity rules (S40 §6.6, §8.6; T263): their list, candidates, writes and preview.</summary>
public interface IVendorEntityMappingService
{
    Task<IReadOnlyList<VendorEntityMappingDto>> ListAsync(int provider, int connectionId, CancellationToken ct = default);

    Task<IReadOnlyList<VendorMappingCandidateDto>> GetCandidatesAsync(int provider, int connectionId,
        CancellationToken ct = default);

    Task<VendorEntityMappingDto> CreateAsync(VendorEntityMappingUpsert request, int userId, CancellationToken ct = default);

    Task<VendorEntityMappingDto> UpdateAsync(int id, VendorEntityMappingUpsert request, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>What <paramref name="request"/> would do on the next sync; <paramref name="mappingId"/> is the rule being edited.</summary>
    Task<VendorEntityMappingPreviewDto> PreviewAsync(VendorEntityMappingUpsert request, int? mappingId,
        CancellationToken ct = default);
}

/// <summary>
/// A refusal of /Integrations/EntityMappings with its stable code (S40 §8.1 shape <c>{ error, message }</c>), mapped by the
/// controller to <see cref="Status"/>. Its own type, not the index's: this is integration configuration, and the index's
/// types are confined to the readers S39 §1.3 allows (<c>IcrIsNotADecisionInputTest</c>).
/// </summary>
public sealed class VendorEntityMappingException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;

    public static VendorEntityMappingException BadRequest(string code, string message) => new(400, code, message);
    public static VendorEntityMappingException Forbidden(string code, string message) => new(403, code, message);
    public static VendorEntityMappingException NotFound(string code, string message) => new(404, code, message);
    public static VendorEntityMappingException Conflict(string code, string message) => new(409, code, message);
}

/// <summary>The stable error codes of /Integrations/EntityMappings (S40 §8.6); the desktop translates each as <c>RiskApi_‹code›MSG</c>.</summary>
public static class VendorEntityMappingErrors
{
    public const string RequiresGlobalScope = "mapping_requires_global_scope";
    public const string InvalidProvider = "invalid_provider";
    public const string InvalidKindForProvider = "invalid_kind_for_provider";
    public const string ConnectionNotFound = "connection_not_found";
    public const string ValueRequired = "value_required";
    public const string ValueTooLong = "value_too_long";
    public const string EntityNotFound = "mapping_entity_not_found";
    public const string InvalidPriority = "invalid_priority";
    public const string InvalidPolicy = "invalid_policy";
    public const string Duplicate = "duplicate_mapping";
    public const string NotFound = "mapping_not_found";
}

/// <summary>
/// T263 (S40 §6.6, §8.6). Reading is open to the controller's <c>configuration</c> holders; every write and the preview need
/// global scope as well, because a rule moves hosts — and with them who sees them — between entities: a scoped holder could
/// otherwise map a tag to their own unit and pull other units' hosts into view (the same rule as risk-context writes,
/// S40 §6.7). Only Vision One rules are accepted until the Tenable connection exists (T265, T268).
/// </summary>
public sealed class VendorEntityMappingService(IDalService dalService, ILogger logger) : IVendorEntityMappingService
{
    public const int MinPriority = 1;
    public const int MaxPriority = 9999;
    private const int SampleSize = 20;
    private const int MaxCandidates = 500;

    public async Task<IReadOnlyList<VendorEntityMappingDto>> ListAsync(int provider, int connectionId,
        CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        var kind = (IntegrationKind)provider;
        var rows = await db.VendorEntityMappings.AsNoTracking()
            .Where(m => m.Provider == kind && m.ConnectionId == connectionId)
            .OrderBy(m => m.Priority).ThenBy(m => m.Id)
            .ToListAsync(ct);
        var names = await EntityNamesAsync(db, rows.Select(r => r.EntityId), ct);
        return rows.Select(r => ToDto(r, names)).ToList();
    }

    public async Task<IReadOnlyList<VendorMappingCandidateDto>> GetCandidatesAsync(int provider, int connectionId,
        CancellationToken ct = default)
    {
        if ((IntegrationKind)provider != IntegrationKind.TrendMicroVisionOne) return [];

        // Read through the host filter (S40 §7.4): a tag seen only on hosts outside the caller's scope is not offered.
        await using var db = dalService.GetContext();
        var tagLists = await db.HostExternalIds.AsNoTracking()
            .Where(x => x.Provider == TrendMicroService.ProviderName && x.ConnectionId == connectionId && x.Tags != null)
            .Select(x => x.Tags!)
            .ToListAsync(ct);

        var seen = new Dictionary<string, (string Spelling, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tagLists.SelectMany(ReadTags))
            seen[tag] = seen.TryGetValue(tag, out var known) ? (known.Spelling, known.Count + 1) : (tag, 1);

        return seen.Values
            .OrderByDescending(v => v.Count).ThenBy(v => v.Spelling, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCandidates)
            .Select(v => new VendorMappingCandidateDto(VendorMatchKind.V1CustomTag, v.Spelling, null, v.Count))
            .ToList();
    }

    public async Task<VendorEntityMappingDto> CreateAsync(VendorEntityMappingUpsert request, int userId,
        CancellationToken ct = default)
    {
        RequireGlobalScope();
        await using var db = dalService.GetContext();
        var value = await ValidateAsync(db, request, ct);
        await RefuseDuplicateAsync(db, request, value, null, ct);

        var row = new VendorEntityMapping
        {
            Provider = (IntegrationKind)request.Provider, ConnectionId = request.ConnectionId, MatchKind = request.Kind,
            MatchValue = value, MatchLabel = Label(request.Label), EntityId = request.EntityId, Priority = request.Priority,
            RestampPolicy = request.Policy, Enabled = request.Enabled, CreatedById = userId > 0 ? userId : null,
            CreatedAt = DateTime.UtcNow
        };
        db.VendorEntityMappings.Add(row);
        await db.SaveChangesAsync(ct);

        logger.Information("Vendor mapping {Id} created: {Provider}/{Connection} {Kind} '{Value}' -> entity {Entity}",
            row.Id, row.Provider, row.ConnectionId, row.MatchKind, row.MatchValue, row.EntityId);
        return ToDto(row, await EntityNamesAsync(db, [row.EntityId], ct));
    }

    public async Task<VendorEntityMappingDto> UpdateAsync(int id, VendorEntityMappingUpsert request,
        CancellationToken ct = default)
    {
        RequireGlobalScope();
        await using var db = dalService.GetContext();
        var row = await db.VendorEntityMappings.FirstOrDefaultAsync(m => m.Id == id, ct)
                  ?? throw VendorEntityMappingException.NotFound(VendorEntityMappingErrors.NotFound, "The mapping does not exist.");
        var value = await ValidateAsync(db, request, ct);
        await RefuseDuplicateAsync(db, request, value, id, ct);

        row.Provider = (IntegrationKind)request.Provider;
        row.ConnectionId = request.ConnectionId;
        row.MatchKind = request.Kind;
        row.MatchValue = value;
        row.MatchLabel = Label(request.Label);
        row.EntityId = request.EntityId;
        row.Priority = request.Priority;
        row.RestampPolicy = request.Policy;
        row.Enabled = request.Enabled;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.Information("Vendor mapping {Id} updated", id);
        return ToDto(row, await EntityNamesAsync(db, [row.EntityId], ct));
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        RequireGlobalScope();
        await using var db = dalService.GetContext();
        var row = await db.VendorEntityMappings.FirstOrDefaultAsync(m => m.Id == id, ct)
                  ?? throw VendorEntityMappingException.NotFound(VendorEntityMappingErrors.NotFound, "The mapping does not exist.");
        // The hosts the rule attributed keep their entity (S40 §6.6): deleting a rule is not an instruction to move them.
        db.VendorEntityMappings.Remove(row);
        await db.SaveChangesAsync(ct);
        logger.Information("Vendor mapping {Id} deleted", id);
    }

    public async Task<VendorEntityMappingPreviewDto> PreviewAsync(VendorEntityMappingUpsert request, int? mappingId,
        CancellationToken ct = default)
    {
        RequireGlobalScope();
        await using var db = dalService.GetContext();
        var value = await ValidateAsync(db, request, ct);
        if (!VendorEntityMappingRules.Attributes(request.Kind)) return new VendorEntityMappingPreviewDto(0, 0, 0, 0, 0, []);

        var kind = (IntegrationKind)request.Provider;
        var excluded = mappingId ?? 0;
        var others = await db.VendorEntityMappings.AsNoTracking()
            .Where(m => m.Provider == kind && m.ConnectionId == request.ConnectionId && m.Enabled && m.Id != excluded)
            .Select(m => new MappingRule(m.Id, m.MatchKind, m.MatchValue, m.EntityId, m.Priority, m.RestampPolicy))
            .ToListAsync(ct);
        // The rule being edited is replaced by the request, whatever its id; a new rule loses a priority tie to a saved one.
        var candidate = new MappingRule(mappingId ?? int.MaxValue, request.Kind, value, request.EntityId, request.Priority,
            request.Policy);
        var ordered = VendorEntityMappingRules.Order(others
            .Where(o => !(o.Kind == request.Kind && VendorTagText.Same(o.Value, value)))
            .Append(candidate));

        var hosts = await (from x in db.HostExternalIds.AsNoTracking()
                where x.Provider == TrendMicroService.ProviderName && x.ConnectionId == request.ConnectionId &&
                      x.Tags != null
                join h in db.Hosts.AsNoTracking() on x.HostId equals h.Id
                select new { h.Id, h.HostName, h.EntityId, x.Tags })
            .ToListAsync(ct);

        int matched = 0, assign = 0, reassign = 0, conflicts = 0, already = 0;
        var sample = new List<(string Label, int? Current, int? New, string Outcome)>();
        foreach (var host in hosts)
        {
            var tags = ReadTags(host.Tags).ToList();
            if (!VendorEntityMappingRules.Matches(candidate, tags)) continue;
            matched++;

            var winner = VendorEntityMappingRules.Winner(ordered, tags)!;
            string outcome;
            int? next = host.EntityId;
            if (winner.Id != candidate.Id)
            {
                conflicts++;
                outcome = VendorMappingOutcomes.Conflict;
            }
            else
            {
                switch (VendorEntityMappingRules.Decide(host.EntityId, candidate))
                {
                    case MappingOutcome.Assign:
                        assign++;
                        outcome = VendorMappingOutcomes.Assign;
                        next = candidate.EntityId;
                        break;
                    case MappingOutcome.Reassign:
                        reassign++;
                        outcome = VendorMappingOutcomes.Reassign;
                        next = candidate.EntityId;
                        break;
                    default:
                        already++;
                        outcome = VendorMappingOutcomes.AlreadyAssigned;
                        break;
                }
            }

            if (sample.Count < SampleSize) sample.Add((host.HostName ?? "#" + host.Id, host.EntityId, next, outcome));
        }

        var names = await EntityNamesAsync(db,
            sample.SelectMany(s => new[] { s.Current, s.New }).Where(e => e is not null).Select(e => e!.Value), ct);
        string? Name(int? id) => id is { } e ? names.GetValueOrDefault(e, "#" + e) : null;
        return new VendorEntityMappingPreviewDto(matched, assign, reassign, conflicts, already,
            sample.Select(s => new VendorMappingPreviewSampleDto(s.Label, Name(s.Current), Name(s.New), s.Outcome)).ToList());
    }

    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The tags of a <c>host_external_ids.tags</c> value; an unreadable value reads as none.</summary>
    internal static IEnumerable<string> ReadTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(json) ?? [])
                .Select(VendorTagText.Canonical).Where(t => t is not null).Select(t => t!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void RequireGlobalScope()
    {
        if (!dalService.GetCurrentEntityScope().IsUnrestricted)
            throw VendorEntityMappingException.Forbidden(VendorEntityMappingErrors.RequiresGlobalScope,
                "A vendor mapping moves hosts between entities, so writing or previewing one needs global scope.");
    }

    /// <summary>Validates the request and answers its value in canonical form.</summary>
    private static async Task<string> ValidateAsync(NRDbContext db, VendorEntityMappingUpsert request, CancellationToken ct)
    {
        if (request is null) throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.ValueRequired, "A body is required.");
        if ((IntegrationKind)request.Provider != IntegrationKind.TrendMicroVisionOne)
            throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.InvalidProvider,
                "Only Vision One mappings are accepted; Tenable tags arrive with the Tenable connection (T268).");
        if (request.Kind is not (VendorMatchKind.V1CustomTag or VendorMatchKind.V1AssetGroup))
            throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.InvalidKindForProvider,
                "A Vision One mapping matches a custom tag or an asset group.");
        if (!Enum.IsDefined(request.Policy))
            throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.InvalidPolicy,
                "The restamp policy is OnlyUnassigned (1) or FollowMapping (2).");
        if (request.Priority is < MinPriority or > MaxPriority)
            throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.InvalidPriority,
                $"The priority is between {MinPriority} and {MaxPriority}.");

        var value = VendorTagText.Canonical(request.Value)
                    ?? throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.ValueRequired, "The value is required.");
        if (value.Length > VendorTagText.MaxLength || (request.Label?.Trim().Length ?? 0) > VendorTagText.MaxLength)
            throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.ValueTooLong,
                $"The value and the label hold at most {VendorTagText.MaxLength} characters.");

        if (!await db.TrendMicroConnections.AsNoTracking().AnyAsync(c => c.Id == request.ConnectionId, ct))
            throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.ConnectionNotFound,
                "The Vision One connection does not exist.");
        if (!await db.Entities.AsNoTracking().AnyAsync(e => e.Id == request.EntityId, ct))
            throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.EntityNotFound, "The entity does not exist.");
        return value;
    }

    /// <summary>One rule per provider, connection, kind and value, compared as the sync compares tags (case-insensitive).</summary>
    private static async Task RefuseDuplicateAsync(NRDbContext db, VendorEntityMappingUpsert request, string value, int? self,
        CancellationToken ct)
    {
        var kind = (IntegrationKind)request.Provider;
        var siblings = await db.VendorEntityMappings.AsNoTracking()
            .Where(m => m.Provider == kind && m.ConnectionId == request.ConnectionId && m.MatchKind == request.Kind &&
                        m.Id != (self ?? 0))
            .Select(m => m.MatchValue)
            .ToListAsync(ct);
        if (siblings.Any(s => VendorTagText.Same(s, value)))
            throw VendorEntityMappingException.Conflict(VendorEntityMappingErrors.Duplicate,
                "There is already a rule for this value on this connection.");
    }

    private static string? Label(string? label) => string.IsNullOrWhiteSpace(label) ? null : label.Trim();

    private static async Task<Dictionary<int, string>> EntityNamesAsync(NRDbContext db, IEnumerable<int> ids,
        CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0) return [];
        var rows = await db.EntitiesProperties.AsNoTracking()
            .Where(p => p.Type == "name" && wanted.Contains(p.Entity))
            .Select(p => new { p.Entity, p.Value })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.Entity).ToDictionary(g => g.Key, g => g.First().Value);
    }

    private static VendorEntityMappingDto ToDto(VendorEntityMapping row, IReadOnlyDictionary<int, string> names) =>
        new(row.Id, (int)row.Provider, row.ConnectionId, row.MatchKind, row.MatchValue, row.MatchLabel, row.EntityId,
            names.GetValueOrDefault(row.EntityId, "#" + row.EntityId), row.Priority, row.RestampPolicy, row.Enabled);
}
