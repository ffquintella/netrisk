using System.Security.Claims;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Risks.Chain;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Security;
using ServerServices.Services;
using Tools.Risks;

namespace ServerServices.Governance;

/// <summary>
/// The risk linkage chain (Stage 9.1, S41 §§4–6, T146–T148).
///
/// Scope is never handled here; it is inherited. Every query runs on the caller's scoped context, so
/// the filters on <c>Risk</c>, <c>Host</c> and <c>RiskChainLink</c> decide what exists — a risk or a
/// host outside the caller's scope is a plain not-found, and an inferred count can only include risks
/// the caller can see. Nothing in this class calls IgnoreQueryFilters; <c>RiskChainServiceInMemoryTest</c>
/// holds that statically.
///
/// Hosts need the <c>hosts</c> permission on top of the controller's <c>RequireRiskmanagement</c>
/// policy, checked through <see cref="HostAccess.CanRead"/> before any query that could reveal whether a
/// host exists. Without it the chain would be a lateral route into the host inventory.
/// </summary>
public class RiskChainService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IRiskChainService
{
    public const string EntityNotInChainRule = "entity_not_in_chain";
    public const string LegacyLinkRule = "legacy_link";

    // --- projection --------------------------------------------------------------------------

    public async Task<RiskChainDto> GetRiskChainAsync(int riskId, ClaimsPrincipal? user)
    {
        var canReadHosts = HostAccess.CanRead(user);

        await using var db = DalService.GetContext();

        var risk = await db.Risks.AsNoTracking()
                       .Where(r => r.Id == riskId)
                       .Select(r => new { r.Id, r.EntityId })
                       .FirstOrDefaultAsync()
                   ?? throw new DataNotFoundException("Risk", riskId.ToString());

        var links = await db.RiskChainLinks.AsNoTracking()
            .Where(l => l.RiskId == riskId)
            .ToListAsync();

        var entityIds = links.Where(l => l.EntityId != null).Select(l => l.EntityId!.Value).ToList();
        if (risk.EntityId is { } scopeEntityId) entityIds.Add(scopeEntityId);

        var targets = await LoadEntityTargetsAsync(db, entityIds);
        var hosts = canReadHosts
            ? await LoadHostNamesAsync(db, links.Where(l => l.HostId != null).Select(l => l.HostId!.Value).ToList())
            : new Dictionary<int, string?>();

        var dtos = links.Select(l => ToDto(l, targets, hosts, canReadHosts)).ToList();

        var chain = new RiskChainDto
        {
            RiskId = risk.Id,
            ScopeEntityId = risk.EntityId,
            ScopeEntityName = risk.EntityId is { } id && targets.TryGetValue(id, out var scope) ? scope.Name : null
        };

        foreach (var level in Enum.GetValues<RiskChainLevel>().OrderBy(l => (int)l))
        {
            var atLevel = dtos.Where(d => d.Level == level)
                .OrderBy(d => d.TargetName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Id)
                .ToList();

            chain.Levels.Add(new RiskChainLevelDto { Level = level, Links = atLevel });
            if (atLevel.Count == 0) chain.MissingLevels.Add(level);
        }

        return chain;
    }

    // --- writes ------------------------------------------------------------------------------

    public async Task<RiskChainLinkWriteResult> AddLinkAsync(int riskId, RiskChainLinkCreateDto request,
        int? actingUserId, ClaimsPrincipal? user)
    {
        if (request is null)
            throw new InvalidParameterException("request", "A link request is required.");

        var hasEntity = request.EntityId is not null;
        var hasHost = request.HostId is not null;

        if (hasEntity == hasHost)
            throw new InvalidParameterException("target",
                "A chain link points at exactly one target: give either entityId or hostId.");

        // Before the context is even opened: an existing host, a missing one and one out of scope must
        // all get this same answer, or the route becomes an oracle for which host ids exist.
        if (hasHost && !HostAccess.CanRead(user))
            throw new PermissionInvalidException(HostAccess.Permission, UserIdOf(user, actingUserId),
                "RiskChain.AddLink");

        var canReadHosts = HostAccess.CanRead(user);

        await using var db = DalService.GetContext();

        if (!await db.Risks.AnyAsync(r => r.Id == riskId))
            throw new DataNotFoundException("Risk", riskId.ToString());

        RiskChainLevel level;
        RiskChainLink? existing;

        if (hasEntity)
        {
            var entityId = request.EntityId!.Value;

            var definition = await db.Entities.AsNoTracking()
                                 .Where(e => e.Id == entityId)
                                 .Select(e => e.DefinitionName)
                                 .FirstOrDefaultAsync()
                             ?? throw new DataNotFoundException("Entity", entityId.ToString());

            level = RiskChainSchema.LevelOf(definition) ?? throw NotInChain(definition);

            existing = await db.RiskChainLinks.FirstOrDefaultAsync(l => l.RiskId == riskId && l.EntityId == entityId);
        }
        else
        {
            var hostId = request.HostId!.Value;

            if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
                throw new DataNotFoundException("Host", hostId.ToString());

            level = RiskChainLevel.Asset;

            existing = await db.RiskChainLinks.FirstOrDefaultAsync(l => l.RiskId == riskId && l.HostId == hostId);
        }

        if (existing is not null)
        {
            if (existing.Origin == RiskChainLinkOrigin.Declared)
                throw AlreadyLinked(riskId, request);

            // Promotion: the link was mirrored from the legacy "Entity" field. Declaring it makes it
            // survive the next PUT /Risks/{id}/Entity, which clears every Legacy link it replaces.
            existing.Origin = RiskChainLinkOrigin.Declared;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            Logger.Information("Chain link {LinkId} of risk {RiskId} promoted from Legacy to Declared",
                existing.Id, riskId);

            return new RiskChainLinkWriteResult(await ProjectAsync(db, existing, canReadHosts), Created: false);
        }

        var link = new RiskChainLink
        {
            RiskId = riskId,
            ChainLevel = level,
            EntityId = request.EntityId,
            HostId = request.HostId,
            Origin = RiskChainLinkOrigin.Declared,
            CreatedAt = DateTime.UtcNow,
            CreatedById = actingUserId is > 0 ? actingUserId : null
        };

        db.RiskChainLinks.Add(link);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (RiskChainPersistence.IsDuplicateLink(ex))
        {
            // Lost the race on the unique index to a concurrent POST or a legacy mirror that passed the
            // same check a moment earlier. The link exists; this caller gets what the check would have
            // told them.
            throw AlreadyLinked(riskId, request);
        }

        Logger.Information("Risk {RiskId} linked to {Target} at level {Level}", riskId,
            hasEntity ? $"entity {request.EntityId}" : $"host {request.HostId}", level);

        return new RiskChainLinkWriteResult(await ProjectAsync(db, link, canReadHosts), Created: true);
    }

    public async Task<RiskChainLinkDeleteResult> DeleteLinkAsync(int riskId, int linkId, ClaimsPrincipal? user)
    {
        var canReadHosts = HostAccess.CanRead(user);

        await using var db = DalService.GetContext();

        // Filtered: a link of a risk out of scope, or to a host out of scope, is not found.
        var link = await db.RiskChainLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.RiskId == riskId)
                   ?? throw new DataNotFoundException("RiskChainLink", linkId.ToString());

        if (link.HostId is not null && !canReadHosts)
            throw new PermissionInvalidException(HostAccess.Permission, UserIdOf(user, null), "RiskChain.DeleteLink");

        if (link.Origin == RiskChainLinkOrigin.Legacy)
            throw new RuleBrokenException(
                "This link comes from the risk's Entity field. Remove it there, where it was set.",
                LegacyLinkRule);

        if (link.EntityId is { } entityId)
        {
            var stillLegacy = await db.Risks
                .Where(r => r.Id == riskId)
                .SelectMany(r => r.Entities)
                .AnyAsync(e => e.Id == entityId);

            if (stillLegacy)
            {
                // Deleting would leave a risk_to_entity row with no chain link — the coexistence
                // invariant — and the next legacy save would put it straight back. So the declaration
                // is withdrawn and the link goes back to being the legacy field's mirror.
                link.Origin = RiskChainLinkOrigin.Legacy;
                link.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                Logger.Information("Chain link {LinkId} of risk {RiskId} demoted to Legacy", linkId, riskId);

                return new RiskChainLinkDeleteResult(await ProjectAsync(db, link, canReadHosts));
            }
        }

        db.RiskChainLinks.Remove(link);
        await db.SaveChangesAsync();

        Logger.Information("Chain link {LinkId} of risk {RiskId} deleted", linkId, riskId);

        return new RiskChainLinkDeleteResult(null);
    }

    // --- node queries ------------------------------------------------------------------------

    public async Task<List<RiskChainMatchDto>> GetRisksByEntityAsync(int entityId, bool inferred)
    {
        await using var db = DalService.GetContext();

        var definition = await db.Entities.AsNoTracking()
                             .Where(e => e.Id == entityId)
                             .Select(e => e.DefinitionName)
                             .FirstOrDefaultAsync()
                         ?? throw new DataNotFoundException("Entity", entityId.ToString());

        if (RiskChainSchema.LevelOf(definition) is null) throw NotInChain(definition);

        var matches = new Dictionary<int, RiskChainMatchDto>();

        var direct = await db.RiskChainLinks.AsNoTracking()
            .Where(l => l.EntityId == entityId)
            .Select(l => l.RiskId)
            .Distinct()
            .ToListAsync();

        foreach (var riskId in direct)
            matches[riskId] = new RiskChainMatchDto { RiskId = riskId, Inferred = false };

        if (inferred)
        {
            var graph = await RiskChainGraphLoader.LoadAsync(db);
            var below = graph.Below(entityId);

            if (below.Count > 0)
            {
                var belowIds = below.Keys.ToList();

                var links = await db.RiskChainLinks.AsNoTracking()
                    .Where(l => l.EntityId != null && belowIds.Contains(l.EntityId.Value))
                    .Select(l => new { l.RiskId, EntityId = l.EntityId!.Value })
                    .ToListAsync();

                foreach (var group in links.GroupBy(l => l.RiskId))
                {
                    // A direct link wins: the risk is reported once, as direct.
                    if (matches.ContainsKey(group.Key)) continue;

                    var via = RiskChainGraph.PickVia(group.Select(l => l.EntityId), below);
                    if (via is null) continue;

                    var node = graph.Find(via.Value.NodeId);

                    matches[group.Key] = new RiskChainMatchDto
                    {
                        RiskId = group.Key,
                        Inferred = true,
                        ViaEntityId = via.Value.NodeId,
                        ViaLevel = RiskChainSchema.LevelOf(node?.DefinitionName),
                        ViaEntityName = node?.Name
                    };
                }
            }
        }

        return await FillRisksAsync(db, matches);
    }

    public async Task<List<RiskChainMatchDto>> GetRisksByHostAsync(int hostId, ClaimsPrincipal? user)
    {
        // Before the context is opened, for the same reason as in AddLinkAsync.
        if (!HostAccess.CanRead(user))
            throw new PermissionInvalidException(HostAccess.Permission, UserIdOf(user, null),
                "RiskChain.GetRisksByHost");

        await using var db = DalService.GetContext();

        // Missing and out of scope are the same answer: the host filter hides the second.
        if (!await db.Hosts.AnyAsync(h => h.Id == hostId))
            throw new DataNotFoundException("Host", hostId.ToString());

        var riskIds = await db.RiskChainLinks.AsNoTracking()
            .Where(l => l.HostId == hostId)
            .Select(l => l.RiskId)
            .Distinct()
            .ToListAsync();

        var matches = riskIds.ToDictionary(id => id, id => new RiskChainMatchDto { RiskId = id, Inferred = false });

        return await FillRisksAsync(db, matches);
    }

    // --- coverage ----------------------------------------------------------------------------

    public async Task<CriticalProcessCoverageDto> GetCriticalProcessCoverageAsync()
    {
        await using var db = DalService.GetContext();

        var graph = await RiskChainGraphLoader.LoadAsync(db);

        var links = await db.RiskChainLinks.AsNoTracking()
            .Where(l => l.EntityId != null)
            .Select(l => new { l.RiskId, EntityId = l.EntityId!.Value })
            .ToListAsync();

        // "Open" is the rule GET /Risks applies: anything not Closed.
        var open = (await db.Risks.AsNoTracking()
                .Where(r => r.Status != "Closed")
                .Select(r => r.Id)
                .ToListAsync())
            .ToHashSet();

        // Stage 9.3 (S43 §4.6): a process's criticality comes from its BIA's MTPD when declared, which
        // wins over the declared entity property.
        var biaMtpd = await db.BusinessImpactAnalyses.AsNoTracking()
            .Where(b => b.MtpdMinutes != null)
            .Select(b => new { b.EntityId, Mtpd = b.MtpdMinutes!.Value })
            .ToDictionaryAsync(b => b.EntityId, b => b.Mtpd);

        return CriticalProcessCoverageCalculator.Compute(graph,
            links.Select(l => (l.RiskId, l.EntityId)), open, DateTime.UtcNow,
            isScopeRestricted: !db.EntityScope.IsUnrestricted, biaMtpdByProcess: biaMtpd);
    }

    // --- helpers -----------------------------------------------------------------------------

    private static RuleBrokenException NotInChain(string definition) =>
        new($"An entity of type '{definition}' is not a node of the linkage chain. Units, people, teams " +
            "and classification levels are scope or organisation, not identification.", EntityNotInChainRule);

    private static DataAlreadyExistsException AlreadyLinked(int riskId, RiskChainLinkCreateDto request) =>
        new("netrisk", "risk_chain_links",
            $"{riskId}:{(request.EntityId is not null ? $"entity {request.EntityId}" : $"host {request.HostId}")}",
            "The risk is already linked to this target.");

    private static int UserIdOf(ClaimsPrincipal? user, int? actingUserId)
    {
        if (actingUserId is > 0) return actingUserId.Value;
        return int.TryParse(user?.FindFirst(ClaimTypes.Sid)?.Value, out var id) ? id : 0;
    }

    private async Task<RiskChainLinkDto> ProjectAsync(AuditableContext db, RiskChainLink link, bool canReadHosts)
    {
        var targets = link.EntityId is { } entityId
            ? await LoadEntityTargetsAsync(db, [entityId])
            : new Dictionary<int, (string Definition, string? Name)>();

        var hosts = link.HostId is { } hostId && canReadHosts
            ? await LoadHostNamesAsync(db, [hostId])
            : new Dictionary<int, string?>();

        return ToDto(link, targets, hosts, canReadHosts);
    }

    private static RiskChainLinkDto ToDto(RiskChainLink link,
        IReadOnlyDictionary<int, (string Definition, string? Name)> targets,
        IReadOnlyDictionary<int, string?> hosts, bool canReadHosts)
    {
        var dto = new RiskChainLinkDto
        {
            Id = link.Id,
            RiskId = link.RiskId,
            Level = link.ChainLevel,
            EntityId = link.EntityId,
            HostId = link.HostId,
            Origin = link.Origin,
            CreatedAt = link.CreatedAt,
            CreatedById = link.CreatedById,
            UpdatedAt = link.UpdatedAt
        };

        if (link.EntityId is { } entityId)
        {
            if (targets.TryGetValue(entityId, out var target))
            {
                dto.TargetType = target.Definition;
                dto.TargetName = target.Name;
            }

            return dto;
        }

        dto.TargetType = RiskChainSchema.HostTargetType;

        if (!canReadHosts)
        {
            // The level is reported as present — it is — and nothing identifying the host is.
            dto.HostId = null;
            dto.TargetName = null;
            dto.IsRedacted = true;
            return dto;
        }

        dto.TargetName = link.HostId is { } hostId && hosts.TryGetValue(hostId, out var name) ? name : null;
        return dto;
    }

    private static async Task<Dictionary<int, (string Definition, string? Name)>> LoadEntityTargetsAsync(
        AuditableContext db, IReadOnlyCollection<int> entityIds)
    {
        var result = new Dictionary<int, (string Definition, string? Name)>();
        if (entityIds.Count == 0) return result;

        var ids = entityIds.Distinct().ToList();

        var entities = await db.Entities.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.DefinitionName })
            .ToListAsync();

        var names = (await db.EntitiesProperties.AsNoTracking()
                .Where(p => ids.Contains(p.Entity) && p.Type == RiskChainSchema.NameProperty)
                .Select(p => new { p.Entity, p.Value })
                .ToListAsync())
            .GroupBy(p => p.Entity)
            .ToDictionary(g => g.Key, g => g.First().Value);

        foreach (var entity in entities)
            result[entity.Id] = (entity.DefinitionName, names.GetValueOrDefault(entity.Id));

        return result;
    }

    private static async Task<Dictionary<int, string?>> LoadHostNamesAsync(AuditableContext db,
        IReadOnlyCollection<int> hostIds)
    {
        if (hostIds.Count == 0) return new Dictionary<int, string?>();

        var ids = hostIds.Distinct().ToList();

        var hosts = await db.Hosts.AsNoTracking()
            .Where(h => ids.Contains(h.Id))
            .Select(h => new { h.Id, h.HostName, h.Fqdn, h.Ip })
            .ToListAsync();

        return hosts.ToDictionary(h => h.Id, h => FirstNonBlank(h.HostName, h.Fqdn, h.Ip));
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static async Task<List<RiskChainMatchDto>> FillRisksAsync(AuditableContext db,
        Dictionary<int, RiskChainMatchDto> matches)
    {
        if (matches.Count == 0) return new List<RiskChainMatchDto>();

        var ids = matches.Keys.ToList();

        // Any status: a closed risk is returned with its status, not filtered out.
        var risks = await db.Risks.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.Subject, r.Status })
            .ToListAsync();

        foreach (var risk in risks)
        {
            matches[risk.Id].Subject = risk.Subject;
            matches[risk.Id].Status = risk.Status;
        }

        var visible = risks.Select(r => r.Id).ToHashSet();

        return matches.Values
            .Where(m => visible.Contains(m.RiskId))
            .OrderBy(m => m.Inferred)
            .ThenBy(m => m.RiskId)
            .ToList();
    }
}
