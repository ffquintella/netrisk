using System.Diagnostics;
using System.Globalization;
using System.Text;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using Model.Status;
using ServerServices.Governance;
using ServerServices.RiskIndex.Engine;
using ServerServices.Services;
using Tools.Security;
using ILogger = Serilog.ILogger;

namespace ServerServices.RiskIndex;

public enum RiskIndexSnapshotResult
{
    /// <summary>The day was computed and every row written in one transaction.</summary>
    Written = 1,

    /// <summary>The (day, profile) already has rows: the series is never rewritten (S40 §9.3 step 2).</summary>
    AlreadyComputed = 2,

    /// <summary>No profile is in force at the snapshot instant: nothing is written, and it is not a failure.</summary>
    NoProfileInForce = 3,

    /// <summary>A step failed: nothing was written, the run is logged Failed, and the day is a gap in the trend.</summary>
    Failed = 4
}

/// <summary>What one snapshot wrote, per table.</summary>
public sealed record RiskIndexSnapshotCounts(int Snapshots, int ObjectDays, int Attributions, int NodeMembers,
    int ClassDays, int ClosuresFrozen, int ClosuresReleased, int Bridges = 0)
{
    public int Total => Snapshots + ObjectDays + Attributions + NodeMembers + ClassDays + ClosuresFrozen + Bridges;
}

public sealed record RiskIndexSnapshotOutcome(RiskIndexSnapshotResult Result, DateOnly Date, int? ProfileId,
    RiskIndexSnapshotCounts? Counts = null, string? Error = null);

/// <summary>The daily official snapshot of the Consolidated Risk Index (S39 §9.1; S40 §9.3) — what the 05:00 job runs.</summary>
public interface IRiskIndexSnapshotService
{
    /// <summary>
    /// Computes and stores the snapshot of the latest 05:00 UTC at or before <paramref name="nowUtc"/>, with the profile
    /// in force then. Idempotent per (day, profile). A failed day is an outcome, not an exception (except cancellation).
    /// </summary>
    Task<RiskIndexSnapshotOutcome> RunAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// M53, T247 (S39 §9.1, §9.5; S40 §7.2, §9.3): resolve the profile in force at <c>t_D</c>; stop if the day exists; rebuild
/// the previous snapshot from its rows; compute the day; detect the markers; write every row in one transaction, in
/// batches; log the run as <see cref="IntegrationKind.RiskIndex"/>. Reads under the caller's scope (the job host's is
/// unrestricted, T237) and never bypasses it.
/// </summary>
public sealed partial class RiskIndexSnapshotService(IDalService dalService, IRiskIndexDayCalculator calculator, ILogger logger)
    : IRiskIndexSnapshotService
{
    /// <summary>The rows per <c>SaveChanges</c> inside the one transaction (S40 §9.3 step 5).</summary>
    public const int DefaultBatchSize = 2000;

    /// <summary>How far back the previous snapshot is looked for: a gap of a few days still compares with the last one.</summary>
    public const int PreviousLookbackDays = 31;

    /// <summary>The snapshot instant's time of day, UTC (S39 §9.1).</summary>
    public static readonly TimeSpan SnapshotTimeOfDay = TimeSpan.FromHours(5);

    /// <summary>The batch size; tests lower it to exercise a multi-batch write.</summary>
    internal int BatchSize { get; set; } = DefaultBatchSize;

    /// <summary><c>t_D</c>: the latest 05:00 UTC at or before <paramref name="nowUtc"/>.</summary>
    public static DateTime SnapshotInstant(DateTime nowUtc)
    {
        var utc = nowUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc) : nowUtc.ToUniversalTime();
        var today = DateTime.SpecifyKind(utc.Date + SnapshotTimeOfDay, DateTimeKind.Utc);
        return utc >= today ? today : today.AddDays(-1);
    }

    /// <summary>
    /// The profile in force at <paramref name="instant"/> (S40 §9.3 step 1): Active or Retired, started at or before it and
    /// not retired at or before it — so a suspended profile stops at the next snapshot. On an overlap the latest start wins.
    /// </summary>
    public static Task<RiskIndexProfile?> ProfileInForceAsync(NRDbContext db, DateTime instant, CancellationToken ct = default) =>
        db.RiskIndexProfiles.AsNoTracking()
            .Where(p => (p.Status == RiskIndexProfileStatus.Active || p.Status == RiskIndexProfileStatus.Retired)
                        && p.EffectiveFrom != null && p.EffectiveFrom <= instant
                        && (p.RetiredAt == null || p.RetiredAt > instant))
            .OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

    public async Task<RiskIndexSnapshotOutcome> RunAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var instant = SnapshotInstant(nowUtc);
        var date = DateOnly.FromDateTime(instant);

        RiskIndexProfile? profile;
        await using (var db = dalService.GetContext())
        {
            profile = await ProfileInForceAsync(db, instant, cancellationToken);
            if (profile is null)
            {
                logger.Information("Risk index snapshot of {Date}: no profile in force at {Instant:u}; nothing written",
                    date, instant);
                return new RiskIndexSnapshotOutcome(RiskIndexSnapshotResult.NoProfileInForce, date, null);
            }

            if (await db.RiskIndexSnapshots.AnyAsync(s => s.SnapshotDate == date && s.ProfileId == profile.Id, cancellationToken))
            {
                logger.Information("Risk index snapshot of {Date} with profile v{Version} already exists; not recomputed",
                    date, profile.Version);
                return new RiskIndexSnapshotOutcome(RiskIndexSnapshotResult.AlreadyComputed, date, profile.Id);
            }
        }

        var clock = Stopwatch.StartNew();
        var run = await OpenRunAsync(profile, nowUtc, cancellationToken);

        try
        {
            var parameters = RiskIndexParametersJson.Deserialize(profile.Parameters);
            var previous = await LoadPreviousAsync(date, profile, cancellationToken);
            var frozen = await LoadFrozenClosuresAsync(cancellationToken);

            var day = await calculator.ComputeAsync(new RiskIndexDayRequest
            {
                Parameters = parameters,
                Mode = profile.Mode,
                SnapshotInstant = instant,
                Options = new RiskIndexCollectionOptions
                {
                    IncidentClosureDates = frozen,
                    DivergenceSince = previous?.DivergenceSince ?? new Dictionary<int, DateTime>()
                },
                // "What moved" across a profile change is the bridge's A_cfg (S39 §9.5, T250): no attribution then.
                Previous = previous?.ProfileId == profile.Id ? previous.Day : null
            }, cancellationToken);

            var hash = await ExternalInputsHashAsync(cancellationToken);
            var appetite = previous is null ? [] : await AppetiteChangesAsync(previous.Instant, instant, cancellationToken);

            // The first day of a new profile (S39 §9.5; S40 §9.3 step 4): both versions, the bridge and A_cfg.
            var bridge = previous is not null && previous.ProfileId != profile.Id
                ? await BridgeAsync(previous, profile, parameters, day, instant, frozen, date, cancellationToken)
                : null;

            var counts = await WriteAsync(() => BuildRows(day, profile, parameters, date, hash, previous, appetite, bridge),
                day.IncidentClosureDates, frozen, date, profile.Id, cancellationToken);

            clock.Stop();
            await CloseRunAsync(run, IntegrationSyncStatus.Succeeded, counts, clock.Elapsed, date, profile, null);
            logger.Information(
                "Risk index snapshot of {Date} with profile v{Version}: {Snapshots} node rows, {Objects} object rows, " +
                "{Attributions} attributions, {Members} node members, {ClassDays} class days, {Bridges} bridge rows in " +
                "{Seconds:0.0} s",
                date, profile.Version, counts.Snapshots, counts.ObjectDays, counts.Attributions, counts.NodeMembers,
                counts.ClassDays, counts.Bridges, clock.Elapsed.TotalSeconds);
            return new RiskIndexSnapshotOutcome(RiskIndexSnapshotResult.Written, date, profile.Id, counts);
        }
        catch (Exception ex)
        {
            clock.Stop();
            logger.Error(ex, "Risk index snapshot of {Date} with profile v{Version} failed; nothing was written", date,
                profile.Version);
            await CloseRunAsync(run, IntegrationSyncStatus.Failed, null, clock.Elapsed, date, profile, ex.Message);
            if (ex is OperationCanceledException) throw;
            return new RiskIndexSnapshotOutcome(RiskIndexSnapshotResult.Failed, date, profile.Id, Error: ex.Message);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The previous snapshot (S39 §9.4–§9.5): the latest day before D within the lookback — its profile, mode, inputs hash,
    // closures and GOV.G latch, and, when it used the same profile, its object rows for "what moved".
    // ------------------------------------------------------------------------------------------------------------------

    internal sealed record PreviousSnapshot(
        DateOnly Date,
        DateTime Instant,
        int ProfileId,
        int ProfileVersion,
        RiskIndexMode Mode,
        string ExternalInputsHash,
        IReadOnlyDictionary<int, IReadOnlySet<int>> Closures,
        IReadOnlyDictionary<int, DateTime> DivergenceSince,
        RiskIndexPreviousDay? Day);

    private async Task<PreviousSnapshot?> LoadPreviousAsync(DateOnly date, RiskIndexProfile profile, CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var from = date.AddDays(-PreviousLookbackDays);

        var heads = await db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.SnapshotDate < date && s.SnapshotDate >= from
                        && s.ScopeKind == RiskIndexScopeKind.Organization && s.Category == RiskIndexCategory.Icr)
            .Select(s => new { s.SnapshotDate, s.ProfileId, s.Mode, s.ExternalInputsHash })
            .ToListAsync(ct);
        if (heads.Count == 0) return null;

        var last = heads.Max(h => h.SnapshotDate);
        var head = heads.Where(h => h.SnapshotDate == last)
            .OrderByDescending(h => h.ProfileId == profile.Id).ThenByDescending(h => h.ProfileId).First();
        var version = await db.RiskIndexProfiles.AsNoTracking().Where(p => p.Id == head.ProfileId)
            .Select(p => p.Version).FirstOrDefaultAsync(ct);

        var members = await db.RiskIndexNodeMembers.AsNoTracking()
            .Where(m => m.SnapshotDate == last && m.ProfileId == head.ProfileId && m.MemberKind == RiskIndexNodeMemberKind.Closure)
            .Select(m => new { m.NodeEntityId, m.MemberEntityId })
            .ToListAsync(ct);
        var closures = members.GroupBy(m => m.NodeEntityId)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<int>)g.Where(m => m.MemberEntityId is not null)
                .Select(m => m.MemberEntityId!.Value).ToHashSet());

        var stored = await db.RiskIndexObjectDays.AsNoTracking()
            .Where(o => o.SnapshotDate == last && o.ProfileId == head.ProfileId)
            .Select(o => new
            {
                o.ObjectKind, o.ObjectRef, o.Category, o.Population, o.RValue, o.Weight, o.Criticality, o.CriticalitySource,
                o.State, o.EntityId, o.Attribution, o.AssetClass, o.SourceMask, o.Freshness, o.CoverageEffect, o.RMax,
                o.ClearedR,
                ScenarioInputs = o.Category == RiskIndexCategory.Gov && o.ObjectKind == RiskIndexObjectKind.Scenario
                    ? o.ScenarioInputs
                    : null
            })
            .ToListAsync(ct);

        // GOV.G's latch (S39 §6.3) is a fact about the risk, carried across a profile change too.
        var divergence = new Dictionary<int, DateTime>();
        foreach (var row in stored.Where(r => r.ScenarioInputs is not null))
        {
            if (RiskIndexStoredJson.ParseScenarioInputs(row.ScenarioInputs)?.DivergenceSince is { } since &&
                int.TryParse(row.ObjectRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var riskId))
                divergence[riskId] = since;
        }

        // Built for the same profile ("what moved") and for the previous one (the bridge's old-version attribution).
        RiskIndexPreviousDay? day = null;
        {
            var categories = await db.RiskIndexSnapshots.AsNoTracking()
                .Where(s => s.SnapshotDate == last && s.ProfileId == head.ProfileId
                            && s.ScopeKind == RiskIndexScopeKind.Organization && s.Category != RiskIndexCategory.Icr)
                .Select(s => new { s.Category, s.Chips })
                .ToListAsync(ct);
            var chips = categories.ToDictionary(c => c.Category,
                c => RiskIndexStoredJson.ParseChips(c.Chips).Select(x => x.Key).ToHashSet(StringComparer.Ordinal));

            day = new RiskIndexPreviousDay
            {
                Objects = stored.Select(o => new RiskIndexObjectRow
                {
                    Key = new IcrObjectKey(o.ObjectKind, o.ObjectRef), Category = o.Category, Population = o.Population,
                    R = o.RValue, Weight = (double)o.Weight, Criticality = o.Criticality, CriticalitySource = o.CriticalitySource,
                    State = o.State, ScopeEntityId = o.EntityId, Attribution = RiskIndexStoredJson.ParseAttribution(o.Attribution),
                    AssetClass = o.AssetClass, SourceMask = o.SourceMask, Freshness = o.Freshness,
                    CoverageEffect = o.CoverageEffect, RMax = o.RMax, ClearedR = o.ClearedR
                }).ToList(),
                Closures = closures.ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => kv.Value),
                SourceEnabled = chips.Count == 0
                    ? null
                    : chips.ToDictionary(kv => kv.Key,
                        kv => !kv.Value.Contains(RiskIndexChipKeys.SourceDisabled) &&
                              !kv.Value.Contains(RiskIndexChipKeys.NoIncidentIntake)),
                IncidentIntakeActive = chips.TryGetValue(RiskIndexCategory.Ame, out var ame)
                    ? !ame.Contains(RiskIndexChipKeys.NoIncidentIntake)
                    : null
            };
        }

        var at = DateTime.SpecifyKind(last.ToDateTime(TimeOnly.MinValue) + SnapshotTimeOfDay, DateTimeKind.Utc);
        return new PreviousSnapshot(last, at, head.ProfileId, version, head.Mode, head.ExternalInputsHash, closures,
            divergence, day);
    }

    /// <summary>The closure dates frozen by earlier snapshots (S39 §9.2).</summary>
    private async Task<Dictionary<int, DateTime>> LoadFrozenClosuresAsync(CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        return await db.RiskIndexIncidentClosures.AsNoTracking().ToDictionaryAsync(c => c.IncidentId, c => c.ClosedAt, ct);
    }

    /// <summary>
    /// SHA-256 of what the index reads but the profile does not copy (S39 §9.5): the SLA configurations, the quantitative
    /// band thresholds and the residual strategy. A change between two snapshots is an ExternalInput marker.
    /// </summary>
    internal async Task<string> ExternalInputsHashAsync(CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var sla = await db.SlaConfigurations.AsNoTracking().OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.Severity, s.MaxTriageDays, s.MaxRemediationDays, s.EntityId, s.EffectiveFrom, s.EffectiveTo })
            .ToListAsync(ct);
        string[] names = [QuantitativeRiskService.BandThresholdsSetting, RiskCalculationService.ResidualStrategySetting];
        var settings = await db.Settings.AsNoTracking().Where(s => names.Contains(s.Name))
            .Select(s => new { s.Name, s.Value }).ToListAsync(ct);

        var text = new StringBuilder();
        foreach (var s in sla)
            text.Append(CultureInfo.InvariantCulture,
                $"sla|{s.Id}|{s.Severity}|{s.MaxTriageDays}|{s.MaxRemediationDays}|{s.EntityId}|{s.EffectiveFrom:O}|{s.EffectiveTo:O}\n");
        foreach (var s in settings.OrderBy(s => s.Name, StringComparer.Ordinal))
            text.Append(CultureInfo.InvariantCulture, $"setting|{s.Name}|{s.Value}\n");
        return HashTool.CreateSha256(text.ToString());
    }

    /// <summary>
    /// The appetites changed between the two snapshot instants, by entity (null: the global ceiling), from the governance
    /// trail — <c>RiskAppetite</c> is audited (S39 §9.5). An appetite whose entity cannot be told counts as global.
    /// </summary>
    private async Task<IReadOnlyList<int?>> AppetiteChangesAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var changes = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(RiskAppetite) && a.OccurredAt > from && a.OccurredAt <= to)
            .Select(a => new { a.EntityId, a.Field, a.OldValue, a.NewValue })
            .ToListAsync(ct);
        if (changes.Count == 0) return [];

        var ids = changes.Select(c => c.EntityId).Distinct().ToList();
        var current = await db.RiskAppetites.AsNoTracking().Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.EntityId, ct);

        var entities = new List<int?>();
        foreach (var id in ids)
        {
            var values = changes.Where(c => c.EntityId == id && c.Field == nameof(RiskAppetite.EntityId))
                .SelectMany(c => new[] { c.OldValue, c.NewValue })
                .Select(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var e) ? e : (int?)null)
                .Where(e => e is not null)
                .ToList();
            if (current.TryGetValue(id, out var entity)) values.Add(entity);

            if (values.Count == 0 || values.Any(v => v is null)) entities.Add(null);
            entities.AddRange(values.Where(v => v is not null).Distinct());
        }

        return entities;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Rows
    // ------------------------------------------------------------------------------------------------------------------

    internal sealed record SnapshotRows(
        List<RiskIndexSnapshot> Snapshots,
        List<RiskIndexObjectDay> ObjectDays,
        List<RiskIndexObjectAttribution> Attributions,
        List<RiskIndexNodeMember> NodeMembers,
        List<RiskIndexClassDay> ClassDays,
        List<RiskIndexBridge> Bridges);

    internal static SnapshotRows BuildRows(RiskIndexDay day, RiskIndexProfile profile, RiskIndexParameters parameters,
        DateOnly date, string hash, PreviousSnapshot? previous, IReadOnlyList<int?> appetite, BridgeDay? bridge = null)
    {
        var now = DateTime.UtcNow;
        var global = GlobalMarkers(profile, previous, hash);
        var mapIds = day.Nodes.FirstOrDefault(n => n.Node.ScopeKind == RiskIndexScopeKind.Organization)?.Members.Closure
                     ?? new HashSet<int>();

        var snapshots = new List<RiskIndexSnapshot>();
        foreach (var node in day.Nodes)
        {
            // "Unassigned" is official-derived from the attribution rows (S40 §5.0), not a pre-computed node.
            if (node.Node.Unassigned) continue;

            var markers = new List<StoredMarker>(global);
            if (previous is not null)
            {
                if (AppetiteMarker(node, appetite) is { } m) markers.Add(m);
                if (EntityMapMarker(node, previous) is { } e) markers.Add(e);
            }

            var change = node.Change ?? bridge?.Changes.GetValueOrDefault((node.Node.ScopeKind, node.Node.ScopeRef));
            snapshots.Add(HeadlineRow(node, change, profile, date, hash, now, markers, previous, HeadlineChips(node, day, parameters)));
            snapshots.AddRange(node.Result.Categories.Select(c =>
                CategoryRow(node, c, profile, date, hash, now, CategoryChips(c, day, parameters))));
        }

        var objectDays = day.Objects.Select(o => new RiskIndexObjectDay
        {
            SnapshotDate = date, ProfileId = profile.Id, ObjectKind = o.Key.Kind, ObjectRef = o.Key.Ref, Category = o.Category,
            Population = o.Population, RValue = IcrMath.Clamp01(o.R), Weight = Decimal4(o.Weight),
            Criticality = (sbyte)Math.Clamp(o.Criticality, 1, 5), CriticalitySource = Truncate(o.CriticalitySource, 32),
            CriticalityAuthorId = o.CriticalityAuthorId, State = o.State, EntityId = o.ScopeEntityId,
            Attribution = RiskIndexStoredJson.Attribution(o.Attribution), Factors = RiskIndexStoredJson.Factors(o.Factors),
            ScenarioInputs = RiskIndexStoredJson.ScenarioInputs(o.ScenarioInputs),
            Signals = RiskIndexStoredJson.Signals(o.Signals), AssetClass = o.AssetClass, SourceMask = o.SourceMask,
            Environment = o.Environment is null ? null : Truncate(o.Environment, 32),
            Freshness = IcrMath.Clamp01(o.Freshness), CoverageEffect = IcrMath.Clamp01(o.CoverageEffect),
            RMax = double.IsFinite(o.RMax) ? Math.Max(0, o.RMax) : 1, ClearedR = IcrMath.Clamp01(o.ClearedR), CreatedAt = now
        }).ToList();

        // One row per object and entity of attribution the map holds; none files it under "Unassigned" (S39 §10.2).
        var attributions = new List<RiskIndexObjectAttribution>();
        foreach (var group in day.Objects.GroupBy(o => o.Key))
        {
            var scope = group.First().ScopeEntityId;
            var entities = group.SelectMany(o => o.Attribution).Where(mapIds.Contains).Distinct().OrderBy(e => e).ToList();
            if (entities.Count == 0) attributions.Add(Attribution(group.Key, null, scope, profile.Id, date, now));
            else attributions.AddRange(entities.Select(e => Attribution(group.Key, e, scope, profile.Id, date, now)));
        }

        // Each entity node's closure and the scope entities of its objects (S40 §9.3, §11.3); a null scope member stands
        // for objects whose source row has none — only an unrestricted reader passes those.
        var members = new List<RiskIndexNodeMember>();
        foreach (var node in day.Nodes.Where(n => n.Node.ScopeKind == RiskIndexScopeKind.Entity && n.Node.EntityId is not null))
        {
            var id = node.Node.EntityId!.Value;
            members.AddRange(node.Members.Closure.OrderBy(e => e).Select(e => Member(id, RiskIndexNodeMemberKind.Closure, e)));
            members.AddRange(node.Members.ScopeEntities.OrderBy(e => e).Select(e => Member(id, RiskIndexNodeMemberKind.Scope, e)));
            if (node.Members.HasUnscopedObjects) members.Add(Member(id, RiskIndexNodeMemberKind.Scope, null));
        }

        var classDays = day.ClassDays.Select(c => new RiskIndexClassDay
        {
            SnapshotDate = date, ProfileId = profile.Id, NodeEntityId = c.NodeEntityId, TabGroup = c.TabGroup,
            Category = c.Category, Value = Math.Clamp(c.Value, 0, 100), CreatedAt = now
        }).ToList();

        var bridges = bridge?.Rows.Select(b => new RiskIndexBridge
        {
            FromProfileId = bridge.FromProfileId, ToProfileId = profile.Id, BridgeDate = date, ScopeKind = b.ScopeKind,
            ScopeRef = b.ScopeRef, EntityId = b.EntityId, Category = b.Category, ValueOld = b.Old, ValueNew = b.New,
            Overlay = RiskIndexStoredJson.Serialize(b.Overlay), OverlayPartial = bridge.OverlayPartial, CreatedAt = now
        }).ToList() ?? [];

        return new SnapshotRows(snapshots, objectDays, attributions, members, classDays, bridges);

        RiskIndexNodeMember Member(int node, RiskIndexNodeMemberKind kind, int? member) => new()
        {
            SnapshotDate = date, ProfileId = profile.Id, NodeEntityId = node, MemberKind = kind, MemberEntityId = member,
            CreatedAt = now
        };
    }

    private static RiskIndexObjectAttribution Attribution(IcrObjectKey key, int? entity, int? scope, int profileId, DateOnly date,
        DateTime now) => new()
    {
        SnapshotDate = date, ProfileId = profileId, ObjectKind = key.Kind, ObjectRef = key.Ref, AttributedEntityId = entity,
        ScopeEntityId = scope, CreatedAt = now
    };

    private static RiskIndexSnapshot HeadlineRow(RiskIndexNodeDay node, IcrChangeAttributionResult? change,
        RiskIndexProfile profile, DateOnly date, string hash, DateTime now, IReadOnlyCollection<StoredMarker> markers,
        PreviousSnapshot? previous, IReadOnlyCollection<StoredChip> chips)
    {
        var r = node.Result;
        var (low, high) = Interval(r.IntervalLow, r.IntervalHigh);
        return new RiskIndexSnapshot
        {
            SnapshotDate = date, ProfileId = profile.Id, Mode = profile.Mode, ScopeKind = node.Node.ScopeKind,
            ScopeRef = node.Node.ScopeRef, EntityId = node.Node.EntityId, Category = RiskIndexCategory.Icr,
            ValueUnfloored = Finite(r.Unfloored), ValueFloored = Finite(r.Floored),
            ValueDisplayed = Math.Clamp(r.Displayed ?? 0, 0, 100),
            FloorReasons = null, // the Gate A display floor belongs to the full mode (S39 §7, T286); the interim has none
            Coverage = Fraction(r.Coverage), Freshness = Fraction(r.Freshness), Quality = Fraction(r.Quality),
            IntervalLow = low, IntervalHigh = high, ObjectCount = r.ObjectCount, PresumptionPoints = Finite(r.PresumptionPoints),
            ExternalInputsHash = hash, TopContributions = RiskIndexStoredJson.TopContributions(r),
            ChangeAttribution = previous is null ? null : RiskIndexStoredJson.ChangeAttribution(change, previous.Date),
            DisplayState = (RiskIndexDisplayState)(int)r.DisplayState, Markers = RiskIndexStoredJson.Markers(markers),
            Chips = RiskIndexStoredJson.Chips(chips), CreatedAt = now
        };
    }

    private static RiskIndexSnapshot CategoryRow(RiskIndexNodeDay node, IcrCategoryResult c, RiskIndexProfile profile,
        DateOnly date, string hash, DateTime now, IReadOnlyCollection<StoredChip> chips)
    {
        var available = c.Status == IcrCategoryStatus.Available;
        var value = available ? Math.Clamp(Finite(c.Value), 0, 100) : 0;
        var (low, high) = available ? Interval(c.IntervalLow, c.IntervalHigh) : (null, null);
        return new RiskIndexSnapshot
        {
            SnapshotDate = date, ProfileId = profile.Id, Mode = profile.Mode, ScopeKind = node.Node.ScopeKind,
            ScopeRef = node.Node.ScopeRef, EntityId = node.Node.EntityId, Category = c.Category, ValueUnfloored = value,
            ValueFloored = value, ValueDisplayed = IcrMath.Displayed(value), Coverage = Fraction(c.Coverage),
            Freshness = Fraction(c.Freshness), Quality = Fraction(c.Coverage * c.Freshness), IntervalLow = low,
            IntervalHigh = high, ObjectCount = c.ObjectCount, PresumptionPoints = available ? Finite(c.PresumptionPoints) : 0,
            ExternalInputsHash = hash, CategoryState = (RiskIndexCategoryState)(int)c.Status,
            Chips = RiskIndexStoredJson.Chips(chips), CreatedAt = now
        };
    }

    private static List<StoredChip> CategoryChips(IcrCategoryResult c, RiskIndexDay day, RiskIndexParameters p)
    {
        var chips = new List<StoredChip>();

        // AME needs a source and an intake; which one is missing is told apart so tomorrow's A_cov reads the same flag.
        var sourceOn = c.Category == RiskIndexCategory.Ame
            ? p.EnabledSources.Contains(RiskIndexSources.Incidents) || p.EnabledSources.Contains(RiskIndexSources.NearMisses)
            : day.SourceEnabled.GetValueOrDefault(c.Category);
        if (!sourceOn && c.Category != RiskIndexCategory.Ter) chips.Add(new StoredChip(RiskIndexChipKeys.SourceDisabled));
        else if (c.Category == RiskIndexCategory.Ame && !day.IncidentIntakeActive)
            chips.Add(new StoredChip(RiskIndexChipKeys.NoIncidentIntake));

        if (c.Partial) chips.Add(new StoredChip(RiskIndexChipKeys.Partial, Args(("note", c.Note))));
        else if (c.Note == "requires M39") chips.Add(new StoredChip(RiskIndexChipKeys.RequiresM39));

        if (c.FloorActive && c.HeldBy.Count > 0)
            chips.Add(new StoredChip(RiskIndexChipKeys.TailFloor,
                Args(("held_by", string.Join(",", c.HeldBy.Select(k => k.ToString()))))));
        if (c.PresumedIncluded > 0)
            chips.Add(new StoredChip(RiskIndexChipKeys.PresumedIncluded,
                Args(("count", RiskIndexStoredJson.Invariant(c.PresumedIncluded)),
                    ("inventory", RiskIndexStoredJson.Invariant(c.InventoryCount)))));
        return chips;
    }

    private static List<StoredChip> HeadlineChips(RiskIndexNodeDay node, RiskIndexDay day, RiskIndexParameters p)
    {
        var r = node.Result;
        var chips = new List<StoredChip>();

        if (r.HeadlineFloorActive &&
            r.Categories.Where(c => c.Status == IcrCategoryStatus.Available).MaxBy(c => c.Value) is { } top)
            chips.Add(new StoredChip(RiskIndexChipKeys.HeadlineFloor,
                Args(("category", RiskIndexStoredJson.Invariant((int)top.Category)))));
        if (r.NoCriticalProcess && node.Node.ScopeKind is RiskIndexScopeKind.Organization or RiskIndexScopeKind.Entity)
            chips.Add(new StoredChip(RiskIndexChipKeys.NoCriticalProcess));
        if (node.Members.ClosureLinkedOnlyThroughEav) chips.Add(new StoredChip(RiskIndexChipKeys.ClosureThroughEav));

        if (node.Node.ScopeKind != RiskIndexScopeKind.Organization) return chips;

        if (day.UnassignedWeightFraction > p.UnassignedThreshold)
            chips.Add(new StoredChip(RiskIndexChipKeys.UnassignedAboveThreshold,
                Args(("fraction", RiskIndexStoredJson.Invariant(day.UnassignedWeightFraction)))));
        chips.Add(new StoredChip(RiskIndexChipKeys.DataQuality, Args(
            ("unassigned_fraction", RiskIndexStoredJson.Invariant(day.UnassignedWeightFraction)),
            ("divergent_links", RiskIndexStoredJson.Invariant(day.DivergentLinks.Count)),
            ("orphan_applications", RiskIndexStoredJson.Invariant(day.OrphanApplications.Count)),
            ("invalid_criticality", RiskIndexStoredJson.Invariant(day.InvalidCriticality.Count)),
            ("legacy_lifecycle_disagreements", RiskIndexStoredJson.Invariant(day.LegacyLifecycleDisagreements)))));
        return chips;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Markers (S40 §9.3): profile, mode and external inputs mark every node; an appetite, the nodes whose closure holds
    // its entity (the global ceiling: all); a changed closure, its node. A first snapshot has nothing to compare with.
    // ------------------------------------------------------------------------------------------------------------------

    private static List<StoredMarker> GlobalMarkers(RiskIndexProfile profile, PreviousSnapshot? previous, string hash)
    {
        var markers = new List<StoredMarker>();
        if (previous is null) return markers;

        if (previous.ProfileId != profile.Id)
            markers.Add(new StoredMarker(RiskIndexMarkerKind.Profile, Args(
                ("from_profile_id", RiskIndexStoredJson.Invariant(previous.ProfileId)),
                ("from_version", RiskIndexStoredJson.Invariant(previous.ProfileVersion)),
                ("to_profile_id", RiskIndexStoredJson.Invariant(profile.Id)),
                ("to_version", RiskIndexStoredJson.Invariant(profile.Version)))));
        if (previous.Mode != profile.Mode)
            markers.Add(new StoredMarker(RiskIndexMarkerKind.Mode, Args(
                ("from", RiskIndexStoredJson.Invariant((int)previous.Mode)),
                ("to", RiskIndexStoredJson.Invariant((int)profile.Mode)))));
        if (!string.Equals(previous.ExternalInputsHash, hash, StringComparison.Ordinal))
            markers.Add(new StoredMarker(RiskIndexMarkerKind.ExternalInput, Args()));
        return markers;
    }

    private static StoredMarker? AppetiteMarker(RiskIndexNodeDay node, IReadOnlyList<int?> appetite)
    {
        if (appetite.Count == 0) return null;
        var global = appetite.Any(e => e is null);
        var root = node.Node.ScopeKind == RiskIndexScopeKind.Organization;
        var touched = appetite.Where(e => e is not null).Select(e => e!.Value)
            .Where(e => root || node.Members.Closure.Contains(e)).Distinct().OrderBy(e => e).ToList();

        if (!global && touched.Count == 0) return null;
        return new StoredMarker(RiskIndexMarkerKind.Appetite, Args(
            ("global", global ? "true" : "false"),
            ("entities", string.Join(",", touched.Select(RiskIndexStoredJson.Invariant)))));
    }

    private static StoredMarker? EntityMapMarker(RiskIndexNodeDay node, PreviousSnapshot previous)
    {
        if (node.Node.ScopeKind != RiskIndexScopeKind.Entity || node.Node.EntityId is not { } id) return null;
        if (!previous.Closures.TryGetValue(id, out var before) || before.SetEquals(node.Members.Closure)) return null;

        return new StoredMarker(RiskIndexMarkerKind.EntityMap, Args(
            ("added", string.Join(",", node.Members.Closure.Except(before).OrderBy(e => e).Select(RiskIndexStoredJson.Invariant))),
            ("removed", string.Join(",", before.Except(node.Members.Closure).OrderBy(e => e).Select(RiskIndexStoredJson.Invariant)))));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The write (S40 §9.3 step 5): one transaction, in batches, through the context's execution strategy — production
    // retries on transient failures, which refuses a bare BeginTransaction — and a retried attempt rebuilds its rows.
    // The in-memory provider of the unit tests has no transactions; DAL.IntegrationTests pins the atomicity on MariaDB.
    // ------------------------------------------------------------------------------------------------------------------

    internal async Task<RiskIndexSnapshotCounts> WriteAsync(Func<SnapshotRows> build, IReadOnlyDictionary<int, DateTime> closures,
        IReadOnlyDictionary<int, DateTime> frozen, DateOnly date, int profileId, CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        if (!db.Database.IsRelational()) return await WriteRowsAsync(db, build(), closures, frozen, date, ct);

        RiskIndexSnapshotCounts? counts = null;
        await db.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
            async token =>
            {
                db.ChangeTracker.Clear();
                counts = await WriteRowsAsync(db, build(), closures, frozen, date, token);
            },
            // A commit whose acknowledgement was lost: the day is there if its rows are.
            async token => await db.RiskIndexSnapshots.AsNoTracking()
                .AnyAsync(s => s.SnapshotDate == date && s.ProfileId == profileId, token),
            ct);
        return counts!;
    }

    private async Task<RiskIndexSnapshotCounts> WriteRowsAsync(AuditableContext db, SnapshotRows rows,
        IReadOnlyDictionary<int, DateTime> closures, IReadOnlyDictionary<int, DateTime> frozen, DateOnly date,
        CancellationToken ct)
    {
        await InsertAsync(db, rows.Snapshots, ct);
        await InsertAsync(db, rows.ObjectDays, ct);
        await InsertAsync(db, rows.Attributions, ct);
        await InsertAsync(db, rows.NodeMembers, ct);
        await InsertAsync(db, rows.ClassDays, ct);
        await InsertAsync(db, rows.Bridges, ct);

        // Freeze the closure dates read for the first time; release those of incidents open again, so a reopened
        // incident freezes anew when it next closes (S39 §9.2).
        var now = DateTime.UtcNow;
        var newlyFrozen = closures.Where(kv => !frozen.ContainsKey(kv.Key))
            .Select(kv => new RiskIndexIncidentClosure { IncidentId = kv.Key, ClosedAt = kv.Value, FrozenOn = date, CreatedAt = now })
            .ToList();
        await InsertAsync(db, newlyFrozen, ct);

        var frozenIds = frozen.Keys.ToList();
        var statuses = await db.Incidents.AsNoTracking().Where(i => frozenIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Status }).ToListAsync(ct);
        var reopened = statuses.Where(i => ClosedStatuses.IsOpen(i.Status))
            .Select(i => new RiskIndexIncidentClosure { IncidentId = i.Id }).ToList();
        if (reopened.Count > 0)
        {
            db.RiskIndexIncidentClosures.RemoveRange(reopened);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return new RiskIndexSnapshotCounts(rows.Snapshots.Count, rows.ObjectDays.Count, rows.Attributions.Count,
            rows.NodeMembers.Count, rows.ClassDays.Count, newlyFrozen.Count, reopened.Count, rows.Bridges.Count);
    }

    private async Task InsertAsync<T>(AuditableContext db, IReadOnlyList<T> rows, CancellationToken ct) where T : class
    {
        for (var offset = 0; offset < rows.Count; offset += BatchSize)
        {
            db.Set<T>().AddRange(rows.Skip(offset).Take(BatchSize));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The run's ledger row (S40 §9.3 step 6), written before the work and settled after it, outside the data transaction,
    // so a failed day still says so. The abandoned-run sweep settles a row a killed process left Running.
    // ------------------------------------------------------------------------------------------------------------------

    private async Task<int?> OpenRunAsync(RiskIndexProfile profile, DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            await using var db = dalService.GetContext();
            var row = new IntegrationSyncLog
            {
                Integration = IntegrationKind.RiskIndex, ConnectionId = profile.Id,
                ConnectionName = $"Risk index profile v{profile.Version}", StartedAt = nowUtc,
                Status = IntegrationSyncStatus.Running
            };
            db.IntegrationSyncLogs.Add(row);
            await db.SaveChangesAsync(ct);
            return row.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning(ex, "Could not record the start of the risk index snapshot in the sync log");
            return null;
        }
    }

    private async Task CloseRunAsync(int? runId, IntegrationSyncStatus status, RiskIndexSnapshotCounts? counts,
        TimeSpan elapsed, DateOnly date, RiskIndexProfile profile, string? error)
    {
        if (runId is null) return;
        try
        {
            await using var db = dalService.GetContext();
            var row = await db.IntegrationSyncLogs.FirstOrDefaultAsync(l => l.Id == runId);
            if (row is null) return;

            row.Status = status;
            row.FinishedAt = DateTime.UtcNow;
            row.CreatedCount = counts?.Total ?? 0;
            row.FailedCount = status == IntegrationSyncStatus.Failed ? 1 : 0;
            row.Summary = counts is null
                ? string.Create(CultureInfo.InvariantCulture,
                    $"Snapshot of {date:yyyy-MM-dd} (profile v{profile.Version}) failed after {elapsed.TotalSeconds:0.0} s; nothing was written.")
                : string.Create(CultureInfo.InvariantCulture,
                    $"Snapshot of {date:yyyy-MM-dd} (profile v{profile.Version}): {counts.Snapshots} node rows, " +
                    $"{counts.ObjectDays} object rows, {counts.Attributions} attributions, {counts.NodeMembers} node members, " +
                    $"{counts.ClassDays} class days, {counts.Bridges} bridge rows, {counts.ClosuresFrozen} closures frozen, " +
                    $"{counts.ClosuresReleased} released " +
                    $"in {elapsed.TotalSeconds:0.0} s.");
            row.ErrorMessage = error is null ? null : Truncate(error, 4000);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Could not record the end of the risk index snapshot in the sync log");
        }
    }

    // ------------------------------------------------------------------------------------------------------------------

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string? Value)[] args) =>
        args.Where(a => a.Value is not null).ToDictionary(a => a.Key, a => a.Value!, StringComparer.Ordinal);

    private static double Finite(double? value) => value is { } v && double.IsFinite(v) ? v : 0;

    private static decimal Fraction(double value) =>
        double.IsFinite(value) ? Math.Round((decimal)Math.Clamp(value, 0, 1), 3, MidpointRounding.AwayFromZero) : 0m;

    /// <summary><c>w_o</c> as <c>decimal(5,4)</c> (S39 §12.5): four places, at most 9.9999 (the weight is at most 1 + β).</summary>
    private static decimal Decimal4(double value) =>
        double.IsFinite(value) ? Math.Min(9.9999m, Math.Round((decimal)Math.Max(0, value), 4, MidpointRounding.AwayFromZero)) : 0m;

    private static (double?, double?) Interval(double? low, double? high)
    {
        if (low is not { } l || high is not { } h || !double.IsFinite(l) || !double.IsFinite(h)) return (null, null);
        return l <= h ? (l, h) : (h, l);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
