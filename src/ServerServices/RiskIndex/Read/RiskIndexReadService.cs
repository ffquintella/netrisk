using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace ServerServices.RiskIndex.Read;

/// <summary>
/// M53, T249 (S40 §8.1–§8.3, §11.3): the read layer over <c>risk_index_snapshots</c> and the object rows. Official values
/// come from the snapshot rows of a pre-computed node whose members the caller's claims cover; everything else (an
/// intersection, an activity, "Unassigned", a node the claims cover in part) is recomposed by the S39 §4 engine from the
/// day's object rows that pass the caller's scope filter — exact over what the caller may see, and never more.
/// </summary>
public sealed partial class RiskIndexReadService(
    IDalService dalService,
    IRiskIndexReadCache cache,
    IRiskIndexDerivedBudget budget,
    ILogger logger,
    ServerServices.Interfaces.IPermissionsService permissionsService) : IRiskIndexReadService
{
    // ------------------------------------------------------------------------------------------------------------------
    // The day, the profile and the publication
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>What every read of one day needs: the date, its profile and parameters, the caller's scope, the map.</summary>
    internal sealed record Day(DateOnly Date, RiskIndexProfile Profile, RiskIndexParameters P, EntityScope Scope, EntityMap Map,
        string ScopeKey);

    public async Task<RiskIndexLatestDto?> GetLatestAsync(CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        var latest = await LatestDateAsync(db, ct);
        if (latest is null) return null;
        var profile = await ProfileOfAsync(db, latest.Value, ct);
        if (profile is null) return null;
        return new RiskIndexLatestDto(latest.Value, profile.Id, profile.Mode, await PublicationStateAsync(db, ct));
    }

    public async Task<IReadOnlyList<DateOnly>> GetSnapshotDatesAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        if (from is { } f && to is { } t && f > t)
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidPeriod, "'from' is after 'to'.");

        await using var db = dalService.GetContext();
        // Node members carry no filter (they are the map a scoped read is checked against) and reveal only that a day exists.
        var members = db.RiskIndexNodeMembers.AsNoTracking().Select(m => m.SnapshotDate);
        var snapshots = db.RiskIndexSnapshots.AsNoTracking().Select(s => s.SnapshotDate);
        if (from is { } a)
        {
            members = members.Where(d => d >= a);
            snapshots = snapshots.Where(d => d >= a);
        }

        if (to is { } b)
        {
            members = members.Where(d => d <= b);
            snapshots = snapshots.Where(d => d <= b);
        }

        var dates = (await members.Distinct().ToListAsync(ct)).Concat(await snapshots.Distinct().ToListAsync(ct));
        return dates.Distinct().Order().ToList();
    }

    public async Task<RiskIndexPublicationDto> GetPublicationAsync(CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        return await PublicationAsync(db, ct);
    }

    public async Task<RiskIndexPublicationDto> SetPublicationAsync(RiskIndexPublicationState state, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        if (!caller.Has(RiskIndexPermissions.Approve))
            throw RiskIndexException.Forbidden("not_permitted", "Changing the publication requires risk_index_approve.");
        if (!Enum.IsDefined(state))
            throw RiskIndexException.BadRequest("invalid_state", "The publication is shadow (1) or published (2).");

        // Publishing or suspending is the committee's decision (S40 §6.4): the grant read from the database, never the
        // administrator flag, which passes the policy attribute in front of this.
        await Profiles.RiskIndexExplicitPermission.RequireAsync(dalService, permissionsService, caller.UserId,
            RiskIndexPermissions.Approve, ct);

        await using var db = dalService.GetContext();
        if (state == RiskIndexPublicationState.Published)
        {
            // Publishing needs an approved profile with the committee's decision (S40 §6.4, §8.2).
            var now = DateTime.UtcNow;
            var approved = await db.RiskIndexProfiles.AsNoTracking().AnyAsync(p =>
                p.Status == RiskIndexProfileStatus.Active && p.CommitteeDecisionRef != null && p.CommitteeDecisionRef != "" &&
                (p.RetiredAt == null || p.RetiredAt > now), ct);
            if (!approved)
                throw RiskIndexException.Conflict(RiskIndexErrors.NoApprovedProfile,
                    "Publishing needs an active profile approved with the committee's decision.");
        }

        var value = state == RiskIndexPublicationState.Published
            ? RiskIndexSettingKeys.PublicationPublished
            : RiskIndexSettingKeys.PublicationShadow;
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Name == RiskIndexSettingKeys.Publication, ct);
        if (row is null) db.Settings.Add(new Setting { Name = RiskIndexSettingKeys.Publication, Value = value });
        else row.Value = value;
        await db.SaveChangesAsync(ct);

        cache.Clear();
        logger.Information("Risk index publication set to {State} by user {UserId}", value, caller.UserId);
        return await PublicationAsync(db, ct);
    }

    private static async Task<RiskIndexPublicationState> PublicationStateAsync(NRDbContext db, CancellationToken ct)
    {
        var value = await db.Settings.AsNoTracking().Where(s => s.Name == RiskIndexSettingKeys.Publication)
            .Select(s => s.Value).FirstOrDefaultAsync(ct);
        // Anything but an explicit "published" is the shadow pilot: the gate fails closed.
        return string.Equals(value?.Trim(), RiskIndexSettingKeys.PublicationPublished, StringComparison.OrdinalIgnoreCase)
            ? RiskIndexPublicationState.Published
            : RiskIndexPublicationState.Shadow;
    }

    private static async Task<RiskIndexPublicationDto> PublicationAsync(NRDbContext db, CancellationToken ct)
    {
        var state = await PublicationStateAsync(db, ct);
        var change = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(Setting) && a.Field == RiskIndexSettingKeys.Publication)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Select(a => new { a.OccurredAt, a.Actor })
            .FirstOrDefaultAsync(ct);
        return new RiskIndexPublicationDto(state, change?.OccurredAt, change?.Actor);
    }

    /// <summary>
    /// The shadow pilot (S39 §13.3; S40 §8.2): while the publication is <c>shadow</c>, an index value goes only to the
    /// committee — explicit <c>risk_index_approve</c> — and to administrators. Checked before any cache lookup.
    /// </summary>
    internal async Task EnsureNotInShadowAsync(RiskIndexCaller caller, CancellationToken ct)
    {
        if (caller.IsAdmin || caller.HoldsExplicitly(RiskIndexPermissions.Approve)) return;
        await using var db = dalService.GetContext();
        if (await PublicationStateAsync(db, ct) == RiskIndexPublicationState.Shadow)
            throw RiskIndexException.Forbidden(RiskIndexErrors.InShadow,
                "The index is in its shadow pilot: only the committee and administrators see it.");
    }

    private static async Task<DateOnly?> LatestDateAsync(NRDbContext db, CancellationToken ct)
    {
        var fromMembers = await db.RiskIndexNodeMembers.AsNoTracking().Select(m => (DateOnly?)m.SnapshotDate).MaxAsync(ct);
        var fromSnapshots = await db.RiskIndexSnapshots.AsNoTracking().Select(s => (DateOnly?)s.SnapshotDate).MaxAsync(ct);
        return fromMembers is null ? fromSnapshots : fromSnapshots is null ? fromMembers
            : fromMembers > fromSnapshots ? fromMembers : fromSnapshots;
    }

    private static async Task<RiskIndexProfile?> ProfileOfAsync(NRDbContext db, DateOnly date, CancellationToken ct)
    {
        var id = await db.RiskIndexSnapshots.AsNoTracking().Where(s => s.SnapshotDate == date).Select(s => (int?)s.ProfileId)
                     .FirstOrDefaultAsync(ct)
                 ?? await db.RiskIndexNodeMembers.AsNoTracking().Where(m => m.SnapshotDate == date)
                     .Select(m => (int?)m.ProfileId).FirstOrDefaultAsync(ct);
        return id is null ? null : await db.RiskIndexProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    internal async Task<Day> ResolveDayAsync(DateOnly? date, CancellationToken ct)
    {
        var scope = dalService.GetCurrentEntityScope();
        var scopeKey = cache.ScopeKey(scope);
        await using var db = dalService.GetContext();

        DateOnly resolved;
        if (date is { } d) resolved = d;
        else
        {
            var latest = await cache.GetOrCreateAsync("latest|" + scopeKey, RiskIndexReadCache.LatestTimeToLive,
                async () => new Box<DateOnly?>(await LatestDateAsync(db, ct)));
            resolved = latest.Value ?? throw RiskIndexException.NotFound(RiskIndexErrors.NoSnapshot, "There is no snapshot yet.");
        }

        var profile = await ProfileOfAsync(db, resolved, ct)
                      ?? throw RiskIndexException.NotFound(RiskIndexErrors.NoSnapshot,
                          string.Create(CultureInfo.InvariantCulture, $"There is no snapshot on {resolved:yyyy-MM-dd}."));
        var map = await RiskIndexObjectCollector.ReadEntityMapAsync(db, ct);
        return new Day(resolved, profile, RiskIndexParametersJson.Deserialize(profile.Parameters), scope, map, scopeKey);
    }

    private sealed record Box<T>(T Value);

    // ------------------------------------------------------------------------------------------------------------------
    // The node: official, official-derived or partial (S40 §5.0, §11.3)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>A resolved node: what it is, how the caller may read it, and where its values come from.</summary>
    internal sealed record Node(
        RiskIndexNodeKey Key,
        RiskIndexNodeRef Ref,
        IReadOnlyList<RiskIndexNodeRef> Breadcrumb,
        RiskIndexProvenance Provenance,
        bool Official,
        RiskIndexScopeKind OfficialKind,
        string OfficialRef,
        int? EntityId,
        IReadOnlySet<int>? Closure,
        bool Precomputed)
    {
        public bool Partial => Provenance == RiskIndexProvenance.PartialRestricted;
        public bool Derived => !Official;
        public bool IsRoot => Key.EntityId is null && !Key.Unassigned;
    }

    internal static RiskIndexNodeKey ParseKey(string? text)
    {
        if (!RiskIndexNodeKey.TryParse(text, out var key))
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidNode,
                "The node key is org, scope, or e:‹id›|u with optional c:, s:, k:, a: terms (S40 §8.1).");
        return key;
    }

    internal async Task<Node> ResolveNodeAsync(RiskIndexNodeKey key, Day day, CancellationToken ct)
    {
        if (key.IsScopeRoot)
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidNode, "'scope' lists the entities of your claims only.");

        await using var db = dalService.GetContext();
        var unrestricted = day.Scope.IsUnrestricted;

        // The root and its intersections are the organization's: global scope only (S39 §10.4).
        if (key.EntityId is null && !key.Unassigned)
        {
            if (!unrestricted)
                throw RiskIndexException.Forbidden(RiskIndexErrors.OrgRequiresGlobalScope,
                    "The organization's headline and its intersections need global scope.");

            var (kind, scopeRef) = key switch
            {
                { HasFilters: false } => (RiskIndexScopeKind.Organization, NodeMembership.RootRef),
                { AssetClass: { } c, TabGroup: null, Source: null, Criticality: null, Environment: null } =>
                    (RiskIndexScopeKind.AssetClass, c.ToString()),
                { Source: { } s, AssetClass: null, TabGroup: null, Criticality: null, Environment: null } =>
                    (RiskIndexScopeKind.Source, s),
                _ => (RiskIndexScopeKind.Organization, "")
            };
            var official = scopeRef != "" && await OfficialRowExistsAsync(db, day, kind, scopeRef, ct);
            return new Node(key, await RefAsync(db, key, day, ct), await BreadcrumbAsync(db, key, day, ct),
                official ? RiskIndexProvenance.Official : RiskIndexProvenance.OfficialDerived, official, kind, scopeRef, null,
                null, official);
        }

        if (key.Unassigned)
            return new Node(key, await RefAsync(db, key, day, ct), await BreadcrumbAsync(db, key, day, ct),
                unrestricted ? RiskIndexProvenance.OfficialDerived : RiskIndexProvenance.PartialRestricted, false,
                RiskIndexScopeKind.Entity, NodeMembership.UnassignedRef, null, new HashSet<int>(), false);

        var id = key.EntityId!.Value;
        if (!day.Map.Contains(id)) throw RiskIndexException.NotFound(RiskIndexErrors.NodeNotFound, "The node does not exist.");
        var entityRef = id.ToString(CultureInfo.InvariantCulture);

        var members = await MembersAsync(db, day, id, ct);
        var precomputed = members.Count > 0 || await OfficialRowExistsAsync(db, day, RiskIndexScopeKind.Entity, entityRef, ct);
        if (precomputed && members.Count == 0 && !unrestricted)
            // A snapshot without its members is never read as "no members" (S40 §9.6): that would pass every claim set.
            throw RiskIndexException.Forbidden(RiskIndexErrors.SnapshotMembershipMissing,
                "This snapshot has no membership record; it can be read with global scope only.");

        IReadOnlySet<int> closure = members.Count > 0
            ? members.Where(m => m.Kind == RiskIndexNodeMemberKind.Closure && m.Entity is not null).Select(m => m.Entity!.Value).ToHashSet()
            : day.Map.Closure(id).Members;

        var covered = unrestricted || (precomputed ? Covers(day.Scope, members) : await AncestorCoversAsync(db, day, id, ct));
        var isOfficial = precomputed && covered && !key.HasFilters;
        var provenance = isOfficial ? RiskIndexProvenance.Official
            : covered ? RiskIndexProvenance.OfficialDerived
            : RiskIndexProvenance.PartialRestricted;

        return new Node(key, await RefAsync(db, key, day, ct), await BreadcrumbAsync(db, key, day, ct), provenance, isOfficial,
            RiskIndexScopeKind.Entity, entityRef, id, closure, precomputed);
    }

    internal sealed record Member(RiskIndexNodeMemberKind Kind, int? Entity);

    private static async Task<List<Member>> MembersAsync(NRDbContext db, Day day, int node, CancellationToken ct) =>
        await db.RiskIndexNodeMembers.AsNoTracking()
            .Where(m => m.SnapshotDate == day.Date && m.ProfileId == day.Profile.Id && m.NodeEntityId == node)
            .Select(m => new Member(m.MemberKind, m.MemberEntityId))
            .ToListAsync(ct);

    /// <summary>
    /// The official-read rule (S40 §11.3): the claims, expanded to descendants, cover every member of the node that day —
    /// its closure and the scope entity of every object attributed to it. A null scope member (an object with no scope
    /// entity) is covered only by global scope.
    /// </summary>
    internal static bool Covers(EntityScope scope, IReadOnlyCollection<Member> members) =>
        scope.IsUnrestricted || (members.Count > 0 && members.All(m => m.Entity is { } e && scope.Allows(e)));

    /// <summary>
    /// The days of [<paramref name="from"/>, <paramref name="to"/>] on which the caller may read the node's official rows: every
    /// day for global scope or a non-entity node, otherwise each day's own members must be covered (S40 §11.3). A trend or
    /// "what moved" spans days, and a node's members change with the map — coverage today says nothing of last month.
    /// </summary>
    internal static async Task<IReadOnlySet<DateOnly>?> CoveredDatesAsync(NRDbContext db, Day day, Node node, DateOnly from,
        DateOnly to, CancellationToken ct)
    {
        if (day.Scope.IsUnrestricted || node.EntityId is not { } id) return null;
        var members = await db.RiskIndexNodeMembers.AsNoTracking()
            .Where(m => m.NodeEntityId == id && m.SnapshotDate >= from && m.SnapshotDate <= to)
            .Select(m => new { m.SnapshotDate, m.MemberKind, m.MemberEntityId })
            .ToListAsync(ct);
        return members.GroupBy(m => m.SnapshotDate)
            .Where(g => Covers(day.Scope, g.Select(m => new Member(m.MemberKind, m.MemberEntityId)).ToList()))
            .Select(g => g.Key).ToHashSet();
    }

    /// <summary>A node with no members of its own (an activity) is covered when its nearest pre-computed ancestor is.</summary>
    private static async Task<bool> AncestorCoversAsync(NRDbContext db, Day day, int id, CancellationToken ct)
    {
        foreach (var ancestor in Ancestors(day.Map, id))
        {
            var members = await MembersAsync(db, day, ancestor, ct);
            if (members.Count > 0) return Covers(day.Scope, members);
        }

        return false;
    }

    private static Task<bool> OfficialRowExistsAsync(NRDbContext db, Day day, RiskIndexScopeKind kind, string scopeRef,
        CancellationToken ct) =>
        db.RiskIndexSnapshots.AsNoTracking().AnyAsync(s => s.SnapshotDate == day.Date && s.ProfileId == day.Profile.Id &&
                                                           s.ScopeKind == kind && s.ScopeRef == scopeRef &&
                                                           s.Category == RiskIndexCategory.Icr, ct);

    private async Task<RiskIndexNodeRef> RefAsync(NRDbContext db, RiskIndexNodeKey key, Day day, CancellationToken ct)
    {
        string label, type;
        if (key.EntityId is { } id)
        {
            var names = await RiskIndexLabels.EntityNamesAsync(db, [id], ct);
            label = names.GetValueOrDefault(id) ?? "#" + id.ToString(CultureInfo.InvariantCulture);
            type = day.Map.DefinitionOf(id) ?? "entity";
        }
        else if (key.Unassigned)
        {
            label = "Unassigned";
            type = "unassigned";
        }
        else
        {
            label = "Organization";
            type = RiskIndexEntityTypes.Organization;
        }

        var filters = FilterLabels(key);
        if (filters.Count > 0)
        {
            label += " ∩ " + string.Join(" ∩ ", filters);
            type += "∩";
        }

        return new RiskIndexNodeRef(key.ToString(), label, type);
    }

    private static List<string> FilterLabels(RiskIndexNodeKey key)
    {
        var labels = new List<string>();
        if (key.AssetClass is { } c) labels.Add(c.ToString());
        if (key.TabGroup is { } t) labels.Add(t.ToString());
        if (key.Source is { } s) labels.Add(s);
        if (key.Criticality is { } k) labels.Add("k" + k.ToString(CultureInfo.InvariantCulture));
        if (key.Environment is { } a) labels.Add(a);
        return labels;
    }

    /// <summary>The path to the node: the organization (global scope only), the entity's ancestors the caller may see.</summary>
    private async Task<IReadOnlyList<RiskIndexNodeRef>> BreadcrumbAsync(NRDbContext db, RiskIndexNodeKey key, Day day,
        CancellationToken ct)
    {
        var path = new List<RiskIndexNodeRef>();
        if (day.Scope.IsUnrestricted)
            path.Add(new RiskIndexNodeRef(RiskIndexNodeKey.Organization, "Organization", RiskIndexEntityTypes.Organization));
        if (key.EntityId is not { } id) return path;

        var ancestors = Ancestors(day.Map, id).Reverse().Where(a => day.Scope.Allows(a)).ToList();
        var names = await RiskIndexLabels.EntityNamesAsync(db, ancestors, ct);
        path.AddRange(ancestors.Select(a => new RiskIndexNodeRef(RiskIndexNodeKey.ForEntity(a).ToString(),
            names.GetValueOrDefault(a) ?? "#" + a.ToString(CultureInfo.InvariantCulture), day.Map.DefinitionOf(a) ?? "entity")));
        return path;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The composition of a node from the day's object rows (under the budget)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>A node recomposed from the visible object rows: its rows, the engine's input and result.</summary>
    internal sealed record Composition(IReadOnlyList<RiskIndexObjectRow> Rows, IcrNodeInput Input, IcrNodeResult Result,
        RiskIndexObjectSet Set);

    internal Task<Composition> ComposeAsync(Node node, Day day, RiskIndexCaller caller, CancellationToken ct) =>
        budget.RunAsync(caller.UserId, async token =>
        {
            await using var db = dalService.GetContext();
            return await ComposeWithinAsync(db, node, day, token);
        }, ct);

    /// <summary>The composition read through <paramref name="db"/>, for a computation that already holds a slot of the budget.</summary>
    private static async Task<Composition> ComposeWithinAsync(NRDbContext db, Node node, Day day, CancellationToken token)
    {
        var set = await RiskIndexStoredDays.LoadSetAsync(db, day.Date, day.Profile.Id, day.P, day.Map, token);
        return Compose(node, day, set);
    }

    /// <summary>The pure part: which objects are the node's, and the S39 §4 formulas over them.</summary>
    internal static Composition Compose(Node node, Day day, RiskIndexObjectSet set)
    {
        var keys = NodeKeys(node, set.Rows, set.Map);
        var rows = set.Rows.Where(r => keys.Contains(r.Key)).ToList();
        var key = node.Key;
        var definition = new RiskIndexNodeDefinition
        {
            ScopeKind = node.IsRoot ? RiskIndexScopeKind.Organization : RiskIndexScopeKind.Entity,
            ScopeRef = node.OfficialRef,
            EntityId = node.EntityId,
            EntityType = node.EntityId is { } id ? set.Map.DefinitionOf(id) : null,
            Unassigned = key.Unassigned,
            AssetClass = key.AssetClass,
            Source = key.Source
        };
        var members = new NodeMembers
        {
            Node = definition,
            Closure = node.Closure ?? set.Map.Ids.ToHashSet(),
            Objects = keys,
            ScopeEntities = rows.Where(r => r.ScopeEntityId is not null).Select(r => r.ScopeEntityId!.Value).ToHashSet(),
            HasUnscopedObjects = rows.Any(r => r.ScopeEntityId is null)
        };
        var assetsOnly = key.TabGroup is not null || key.Environment is not null;
        var input = RiskIndexDayCalculator.BuildNodeInput(members, set, day.Profile.Mode, day.P, assetsOnly);
        return new Composition(rows, input, IcrNodeCalculator.Compute(input, day.P), set);
    }

    /// <summary>
    /// The node's object set (S39 §10.1–§10.2): the deduplicated objects attributed to any entity of its closure — the
    /// root's every object, "Unassigned" those attributed to no entity of the map — intersected with the key's class,
    /// source, criticality and environment terms.
    /// </summary>
    internal static IReadOnlySet<IcrObjectKey> NodeKeys(Node node, IReadOnlyList<RiskIndexObjectRow> rows, EntityMap map)
    {
        var key = node.Key;
        IEnumerable<RiskIndexObjectRow> candidates = node switch
        {
            { IsRoot: true } => rows,
            { Key.Unassigned: true } => rows.Where(r => !r.Attribution.Any(map.Contains)),
            _ => rows.Where(r => r.Attribution.Any(node.Closure!.Contains))
        };

        var keys = candidates.Select(r => r.Key).ToHashSet();
        if (!key.HasFilters) return keys;

        var byKey = rows.Where(r => keys.Contains(r.Key)).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.ToList());
        return keys.Where(k => Matches(byKey[k], key)).ToHashSet();
    }

    internal static bool Matches(IReadOnlyList<RiskIndexObjectRow> rows, RiskIndexNodeKey key)
    {
        var first = rows[0];
        if (key.AssetClass is { } c && first.AssetClass != c) return false;
        if (key.TabGroup is { } t && (first.AssetClass is not { } ac || RiskIndexTabGroups.Of(ac) != t)) return false;
        if (key.Criticality is { } k && rows.All(r => r.Criticality != k)) return false;
        if (key.Environment is { } a && rows.All(r => !string.Equals(r.Environment, a, StringComparison.Ordinal))) return false;
        if (key.Source is { } s && !rows.Any(r => r.Category == RiskIndexCategory.Exp && RiskIndexSourceBits.Has(r.SourceMask, s)))
            return false;
        return true;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------------------------------------------------------

    internal string CacheKey(string endpoint, Node node, Day day, RiskIndexCaller caller, string? extra = null) =>
        string.Join('|', endpoint, node.Key.ToString(), day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            day.Profile.Id.ToString(CultureInfo.InvariantCulture), ((int)day.Profile.Mode).ToString(CultureInfo.InvariantCulture),
            day.ScopeKey, caller.PermissionKey, extra ?? "");

    internal static string BandKey(int index) => "b" + index.ToString(CultureInfo.InvariantCulture);

    internal static IReadOnlyList<RiskIndexBandDto> Bands(RiskIndexParameters p) =>
        p.Bands.Select((b, i) => new RiskIndexBandDto(BandKey(i), b.LabelPtBr, b.LabelEnUs, i, RampStep(i, p.Bands.Count),
            b.LowerInclusive)).ToList();

    /// <summary>
    /// The ramp step of a band's position — the table of S40 §10.4 that the desktop's <c>ChartPaletteMap.BandStep</c> also
    /// encodes, so a band's <c>RampStep</c> here and its pill class there agree: 2 → 1, 5; 3 → 1, 3, 5; 4 → 1, 3, 4, 5;
    /// 5 → 1…5. (Spacing the steps evenly gave 1, 2, 4, 5 for four bands.)
    /// </summary>
    internal static int RampStep(int index, int count) => count switch
    {
        2 => index == 0 ? 1 : 5,
        3 => 1 + 2 * Math.Clamp(index, 0, 2),
        4 => index <= 0 ? 1 : 2 + Math.Clamp(index, 1, 3),
        _ => Math.Clamp(index + 1, 1, 5)
    };

    internal static int? BandIndex(int? displayed, RiskIndexDisplayState state, RiskIndexParameters p)
    {
        if (displayed is not { } d || state is RiskIndexDisplayState.Indicative or RiskIndexDisplayState.NoData) return null;
        var index = Tools.Risks.RiskBand.Faixa(d, p.Bands.Select(b => (double)b.LowerInclusive).ToList());
        return index < 0 ? null : index;
    }

    internal static RiskIndexProfileSummaryDto Summary(RiskIndexProfile p, string? createdBy = null, string? approvedBy = null) =>
        new(p.Id, p.Version, p.Name, p.Preset, p.Status, p.EffectiveFrom, p.RetiredAt, createdBy ?? "", approvedBy, p.IsRebaseline,
            p.CommitteeDecisionRef);

    /// <summary>The entity's ancestors by <c>entities.parent</c>, nearest first; a cycle stops the walk.</summary>
    internal static IEnumerable<int> Ancestors(EntityMap map, int id)
    {
        var seen = new HashSet<int> { id };
        var current = map.Tree.ParentOf(id);
        while (current is { } parent && seen.Add(parent))
        {
            yield return parent;
            current = map.Tree.ParentOf(parent);
        }
    }

    internal static IcrObjectKey? ParseObjectKey(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0) return null;
        return Enum.TryParse<RiskIndexObjectKind>(text[..colon], out var kind) ? new IcrObjectKey(kind, text[(colon + 1)..]) : null;
    }
}
