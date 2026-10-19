using DAL.Enums;
using Microsoft.Extensions.DependencyInjection;
using Model.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using ServerServices.Services;

namespace ServerServices.RiskIndex;

/// <summary>Yesterday's rows, rebuilt by the job from <c>risk_index_object_days</c> and <c>risk_index_node_members</c>.</summary>
public sealed record RiskIndexPreviousDay
{
    public IReadOnlyList<RiskIndexObjectRow> Objects { get; init; } = [];

    /// <summary>Each entity node's closure on that day, by <c>scope_ref</c>; a node missing here uses today's closure.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<int>> Closures { get; init; } = new Dictionary<string, IReadOnlySet<int>>();

    /// <summary>That day's category source flags and incident intake; null reads today's (S39 §9.4 A_cov).</summary>
    public IReadOnlyDictionary<RiskIndexCategory, bool>? SourceEnabled { get; init; }

    public bool? IncidentIntakeActive { get; init; }
}

/// <summary>What the snapshot job asks for: one day computed with one approved profile (S40 §9.3).</summary>
public sealed record RiskIndexDayRequest
{
    public required RiskIndexParameters Parameters { get; init; }
    public RiskIndexMode Mode { get; init; } = RiskIndexMode.Interim;

    /// <summary><c>t_D</c>: the snapshot instant (05:00 UTC of D). Every age, decay and due date is measured to it.</summary>
    public required DateTime SnapshotInstant { get; init; }

    public RiskIndexCollectionOptions Options { get; init; } = new();

    /// <summary>D−1, for "what moved" (S39 §9.4); null computes no attribution.</summary>
    public RiskIndexPreviousDay? Previous { get; init; }
}

/// <summary>One node's day: its members, its headline and composition, and what moved since D−1.</summary>
public sealed record RiskIndexNodeDay
{
    public required RiskIndexNodeDefinition Node { get; init; }
    public required NodeMembers Members { get; init; }
    public required IcrNodeResult Result { get; init; }
    public IcrChangeAttributionResult? Change { get; init; }
}

/// <summary>
/// The <c>C_k</c> of a pre-computed node intersected with an asset-class tab (S40 §5.5.1, §7.2 <c>risk_index_class_days</c>):
/// the root (<see cref="NodeEntityId"/> null) or an entity node; only categories available in the intersection.
/// </summary>
public sealed record RiskIndexClassDayValue(int? NodeEntityId, RiskIndexTabGroup TabGroup, RiskIndexCategory Category,
    double Value);

/// <summary>One full day for a profile: every object row and every pre-computed node (S39 §9.1).</summary>
public sealed record RiskIndexDay
{
    public DateTime SnapshotInstant { get; init; }
    public RiskIndexMode Mode { get; init; }
    public IReadOnlyList<RiskIndexObjectRow> Objects { get; init; } = [];
    public IReadOnlyList<RiskIndexNodeDay> Nodes { get; init; } = [];

    /// <summary>The root and every entity node ∩ each asset-class tab, by available category (T247).</summary>
    public IReadOnlyList<RiskIndexClassDayValue> ClassDays { get; init; } = [];

    /// <summary>Which categories had an enabled source (AME: and intake) — carried to tomorrow's A_cov (S39 §9.4).</summary>
    public IReadOnlyDictionary<RiskIndexCategory, bool> SourceEnabled { get; init; } = new Dictionary<RiskIndexCategory, bool>();

    /// <summary>Freeze these closure dates for tomorrow (S39 §9.2).</summary>
    public IReadOnlyDictionary<int, DateTime> IncidentClosureDates { get; init; } = new Dictionary<int, DateTime>();

    public bool IncidentIntakeActive { get; init; }

    /// <summary>Quality (S39 §8.5–§8.6, §10.1): divergent process links, orphan root applications, out-of-range
    /// criticality, legacy/lifecycle disagreements, and the Unassigned share of hosts and applications by weight.</summary>
    public IReadOnlyList<DivergentLink> DivergentLinks { get; init; } = [];

    public IReadOnlyList<int> OrphanApplications { get; init; } = [];
    public IReadOnlyList<int> InvalidCriticality { get; init; } = [];
    public int LegacyLifecycleDisagreements { get; init; }
    public double UnassignedWeightFraction { get; init; }
}

/// <summary>Computes one day of the index for a profile — the entry point of the snapshot job (T247).</summary>
public interface IRiskIndexDayCalculator
{
    /// <summary>Reads under the caller's entity scope (the job's is unrestricted, T237) and writes nothing.</summary>
    Task<RiskIndexDay> ComputeAsync(RiskIndexDayRequest request, CancellationToken cancellationToken = default);
}

public sealed class RiskIndexDayCalculator(IDalService dalService) : IRiskIndexDayCalculator
{
    private static readonly string[] ExposureSources =
    [
        RiskIndexSources.V1Device, RiskIndexSources.V1Cve, RiskIndexSources.SscTotal, RiskIndexSources.TenableNessus,
        RiskIndexSources.TenableVm, RiskIndexSources.TenableAes, RiskIndexSources.Importers
    ];

    public async Task<RiskIndexDay> ComputeAsync(RiskIndexDayRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = dalService.GetContext(withIdentity: false);
        var objects = await RiskIndexObjectCollector.CollectAsync(db, request.Parameters, request.Mode, request.SnapshotInstant,
            request.Options, cancellationToken);
        return Compose(objects, request);
    }

    /// <summary>The pure half: nodes, membership, results and attribution from a day's object set.</summary>
    /// <param name="options">What to compute beside the node values; the default is everything the snapshot stores. A
    /// replay (the preview, the bridge's overlay) asks for neither class days nor sensitivity.</param>
    public static RiskIndexDay Compose(RiskIndexObjectSet set, RiskIndexDayRequest request, RiskIndexComposeOptions? options = null)
    {
        options ??= RiskIndexComposeOptions.Full;
        var p = request.Parameters;
        var rows = set.Rows;
        var attributions = Attributions(rows);
        var members = NodeMembership.Resolve(set.Map, attributions).ToList();
        members.AddRange(DimensionNodes(rows));

        var previousRows = request.Previous?.Objects;
        var previousAttributions = previousRows is null ? null : Attributions(previousRows);
        var previousKeys = previousRows?.Select(r => r.Key).ToHashSet();
        var todayKeys = rows.Select(r => r.Key).ToHashSet();
        var scenarioEntities = rows.Where(r => r.Category == RiskIndexCategory.Reg).SelectMany(r => r.Attribution).ToHashSet();

        var nodes = new List<RiskIndexNodeDay>(members.Count);
        foreach (var node in members)
        {
            var input = NodeInput(node, rows, set, request.Mode, p, scenarioEntities);
            var result = IcrNodeCalculator.Compute(input, p, options.SensitivityCount);

            IcrChangeAttributionResult? change = null;
            if (previousRows is not null)
            {
                var previousMembers = PreviousMembers(node, set.Map, previousRows, previousAttributions!, request.Previous!);
                var previousNode = node with { Objects = previousMembers };
                var previousScenarioEntities = previousRows.Where(r => r.Category == RiskIndexCategory.Reg)
                    .SelectMany(r => r.Attribution).ToHashSet();
                var previousSet = set with
                {
                    SourceEnabled = request.Previous!.SourceEnabled ?? set.SourceEnabled,
                    IncidentIntakeActive = request.Previous.IncidentIntakeActive ?? set.IncidentIntakeActive
                };
                var before = NodeInput(previousNode, previousRows, previousSet, request.Mode, p, previousScenarioEntities);

                // Reattribution: an object that existed on both days but entered or left this node (S39 §9.4).
                var moved = node.Objects.Except(previousMembers).Concat(previousMembers.Except(node.Objects))
                    .Where(k => todayKeys.Contains(k) && previousKeys!.Contains(k)).ToHashSet();
                change = IcrChangeAttribution.Attribute(before, input, p, moved);
            }

            nodes.Add(new RiskIndexNodeDay { Node = node.Node, Members = node, Result = result, Change = change });
        }

        var assets = rows.Where(r => r.Category is RiskIndexCategory.Exp or RiskIndexCategory.Gov &&
                                     r.Key.Kind is RiskIndexObjectKind.Host or RiskIndexObjectKind.Application or
                                         RiskIndexObjectKind.UnassignedGroup)
            .DistinctBy(r => r.Key).ToList();
        var assetWeight = assets.Sum(r => r.Weight);

        return new RiskIndexDay
        {
            SnapshotInstant = request.SnapshotInstant,
            Mode = request.Mode,
            Objects = rows,
            Nodes = nodes,
            ClassDays = options.ClassDays ? ClassDays(members, rows, set, request.Mode, p, scenarioEntities) : [],
            SourceEnabled = set.SourceEnabled,
            IncidentClosureDates = set.IncidentClosureDates,
            IncidentIntakeActive = set.IncidentIntakeActive,
            DivergentLinks = set.Map.DivergentLinks(),
            OrphanApplications = set.Map.OrphanApplications(),
            InvalidCriticality = set.Map.InvalidCriticality,
            LegacyLifecycleDisagreements = set.LegacyLifecycleDisagreements,
            UnassignedWeightFraction = assetWeight > 0
                ? assets.Where(r => r.Attribution.Count == 0).Sum(r => r.Weight) / assetWeight
                : 0
        };
    }

    /// <summary>
    /// One node's engine input from a day's object set (S39 §10.1–§10.2) — what a derived or partial read (S40 §5.0, §8.1),
    /// a replay and an on-demand "what moved" compose. <paramref name="assetsOnly"/> keeps only asset rows, as an
    /// intersection with an asset class, a source or an environment does (REG and AME are then not applicable).
    /// </summary>
    public static IcrNodeInput BuildNodeInput(NodeMembers node, RiskIndexObjectSet set, RiskIndexMode mode,
        RiskIndexParameters p, bool assetsOnly = false)
    {
        var scenarioEntities = set.Rows.Where(r => r.Category == RiskIndexCategory.Reg).SelectMany(r => r.Attribution).ToHashSet();
        return NodeInput(node, set.Rows, set, mode, p, scenarioEntities, assetsOnly);
    }

    /// <summary>
    /// The root and each entity node intersected with each asset-class tab (S40 §5.5, §7.2): the node's asset rows of the
    /// tab's classes, under the node's own interim rules (a process's EXP partial, an activity's unavailable), valued by
    /// the category formula. Only the tab's categories that are available in the intersection are kept; the Unassigned
    /// node and the root's dimension nodes are derived on read and have none.
    /// </summary>
    private static List<RiskIndexClassDayValue> ClassDays(IReadOnlyList<NodeMembers> nodes, IReadOnlyList<RiskIndexObjectRow> rows,
        RiskIndexObjectSet set, RiskIndexMode mode, RiskIndexParameters p, IReadOnlySet<int> scenarioEntities)
    {
        var byKey = rows.Where(r => r.AssetClass is not null).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.ToList());
        var values = new List<RiskIndexClassDayValue>();

        foreach (var node in nodes)
        {
            var definition = node.Node;
            var root = definition.ScopeKind == RiskIndexScopeKind.Organization;
            if (!root && (definition.ScopeKind != RiskIndexScopeKind.Entity || definition.EntityId is null)) continue;

            var assetRows = node.Objects.Where(byKey.ContainsKey).SelectMany(k => byKey[k]).ToList();
            if (assetRows.Count == 0) continue;

            foreach (var tab in RiskIndexTabGroups.All)
            {
                var tabRows = assetRows.Where(r => RiskIndexTabGroups.Of(r.AssetClass!.Value) == tab).ToList();
                if (tabRows.Count == 0) continue;

                var slice = node with { Objects = tabRows.Select(r => r.Key).ToHashSet() };
                var input = NodeInput(slice, tabRows, set, mode, p, scenarioEntities, assetsOnly: true);
                foreach (var category in input.Categories.Where(c => RiskIndexTabGroups.Categories(tab).Contains(c.Category)))
                {
                    var result = IcrCategoryCalculator.Compute(category, p);
                    if (result.Status != IcrCategoryStatus.Available) continue;
                    values.Add(new RiskIndexClassDayValue(definition.EntityId, tab, category.Category, result.Value));
                }
            }
        }

        return values;
    }

    private static List<ObjectAttribution> Attributions(IEnumerable<RiskIndexObjectRow> rows) =>
        rows.GroupBy(r => r.Key)
            .Select(g => new ObjectAttribution(g.Key, g.SelectMany(r => r.Attribution).Distinct().OrderBy(i => i).ToList(),
                g.First().ScopeEntityId))
            .ToList();

    /// <summary>The root's asset-class and source nodes (S39 §9.1): objects by class, and objects read by an exposure source.</summary>
    private static IEnumerable<NodeMembers> DimensionNodes(IReadOnlyList<RiskIndexObjectRow> rows)
    {
        foreach (var assetClass in rows.Where(r => r.AssetClass is not null).Select(r => r.AssetClass!.Value).Distinct().OrderBy(c => c))
        {
            var objects = rows.Where(r => r.AssetClass == assetClass).ToList();
            yield return Dimension(new RiskIndexNodeDefinition
            {
                ScopeKind = RiskIndexScopeKind.AssetClass, ScopeRef = assetClass.ToString(), AssetClass = assetClass
            }, objects);
        }

        foreach (var source in ExposureSources)
        {
            var keys = rows.Where(r => r.Category == RiskIndexCategory.Exp && RiskIndexSourceBits.Has(r.SourceMask, source))
                .Select(r => r.Key).ToHashSet();
            if (keys.Count == 0) continue;
            yield return Dimension(new RiskIndexNodeDefinition
            {
                ScopeKind = RiskIndexScopeKind.Source, ScopeRef = source, Source = source
            }, rows.Where(r => keys.Contains(r.Key)).ToList());
        }
    }

    private static NodeMembers Dimension(RiskIndexNodeDefinition node, IReadOnlyList<RiskIndexObjectRow> rows) => new()
    {
        Node = node,
        Objects = rows.Select(r => r.Key).ToHashSet(),
        ScopeEntities = rows.Where(r => r.ScopeEntityId is not null).Select(r => r.ScopeEntityId!.Value).ToHashSet(),
        HasUnscopedObjects = rows.Any(r => r.ScopeEntityId is null)
    };

    /// <summary>The node's member set on D−1: yesterday's attributions against yesterday's closure (or today's).</summary>
    private static IReadOnlySet<IcrObjectKey> PreviousMembers(NodeMembers node, EntityMap map,
        IReadOnlyList<RiskIndexObjectRow> rows, IReadOnlyList<ObjectAttribution> attributions, RiskIndexPreviousDay previous)
    {
        var definition = node.Node;
        if (definition.ScopeKind == RiskIndexScopeKind.Organization) return attributions.Select(a => a.Key).ToHashSet();
        if (definition.Unassigned)
            return attributions.Where(a => a.Entities.All(e => !map.Contains(e))).Select(a => a.Key).ToHashSet();
        if (definition.AssetClass is { } assetClass)
            return rows.Where(r => r.AssetClass == assetClass).Select(r => r.Key).ToHashSet();
        if (definition.Source is { } source)
            return rows.Where(r => r.Category == RiskIndexCategory.Exp && RiskIndexSourceBits.Has(r.SourceMask, source))
                .Select(r => r.Key).ToHashSet();

        var closure = previous.Closures.TryGetValue(definition.ScopeRef, out var yesterday) ? yesterday : node.Closure;
        return NodeMembership.ObjectsOf(attributions, closure);
    }

    /// <summary>
    /// A node's engine input (S39 §10.1–§10.2): its members' rows by category. Interim mode links no host to a process or an
    /// activity (M39): a process's EXP is partial (its applications' host-less findings), an activity's is unavailable.
    /// Class and source nodes keep only asset rows; REG and AME are then "not applicable".
    /// </summary>
    private static IcrNodeInput NodeInput(NodeMembers node, IReadOnlyList<RiskIndexObjectRow> rows, RiskIndexObjectSet set,
        RiskIndexMode mode, RiskIndexParameters p, IReadOnlySet<int> scenarioEntities, bool assetsOnly = false)
    {
        var definition = node.Node;
        var type = definition.EntityType;
        var interimProcess = mode == RiskIndexMode.Interim && type == RiskIndexEntityTypes.BusinessProcess;
        var interimActivity = mode == RiskIndexMode.Interim && type == RiskIndexEntityTypes.Activity;
        var dimension = assetsOnly || definition.AssetClass is not null || definition.Source is not null;

        bool Keep(RiskIndexObjectRow r)
        {
            if (!node.Objects.Contains(r.Key)) return false;
            if ((interimProcess || interimActivity) && r.Population is RiskIndexPopulation.Assets &&
                r.Key.Kind is RiskIndexObjectKind.Host or RiskIndexObjectKind.ExternalDomain) return false;
            if (interimActivity && r.Population is RiskIndexPopulation.Assets) return false;
            if (dimension && r.Population is RiskIndexPopulation.Scenarios or RiskIndexPopulation.Targets) return false;
            if (dimension && r.Category == RiskIndexCategory.Ctl && r.Key.Kind != RiskIndexObjectKind.Host) return false;
            return true;
        }

        var kept = rows.Where(Keep).ToList();
        var categories = IcrNodeCalculator.Categories.Select(category => new IcrCategoryInput
        {
            Category = category,
            Objects = kept.Where(r => r.Category == category).Select(r => r.ToEngine()).ToList(),
            SourceEnabled = set.SourceEnabled.GetValueOrDefault(category),
            ForcedUnavailable = category == RiskIndexCategory.Exp && interimActivity,
            Partial = category == RiskIndexCategory.Exp && interimProcess,
            Note = category == RiskIndexCategory.Exp && (interimProcess || interimActivity) ? "requires M39"
                : category == RiskIndexCategory.Ame && !set.IncidentIntakeActive ? "no incident intake"
                : null
        }).ToList();

        // cov_processos (S39 §8.4): the critical processes (declared ≥ 4) in the closure, and those with an open scenario.
        double critical = 0, covered = 0;
        if (!dimension && !definition.Unassigned)
        {
            var processes = definition.ScopeKind == RiskIndexScopeKind.Organization
                ? set.Map.Ids
                : node.Closure;
            foreach (var process in processes.Where(id => set.Map.DefinitionOf(id) == RiskIndexEntityTypes.BusinessProcess))
            {
                if (set.Map.Criticality(process) is not { } c || c < 4) continue;
                var m = IcrMath.Modulator(c, p.MA);
                critical += m;
                if (set.Map.Tree.SelfAndDescendants(process).Any(scenarioEntities.Contains)) covered += m;
            }
        }

        return new IcrNodeInput
        {
            Categories = categories,
            CriticalProcessWeight = critical,
            CriticalProcessWeightCovered = covered
        };
    }
}

/// <summary>What <see cref="RiskIndexDayCalculator.Compose"/> computes beside the node values.</summary>
/// <param name="ClassDays">The node ∩ asset-class tab values (<c>risk_index_class_days</c>).</param>
/// <param name="SensitivityCount">How many contributions get an indicator sensitivity; null is the profile's
/// <c>top_contribuicoes</c>, 0 none.</param>
public sealed record RiskIndexComposeOptions(bool ClassDays = true, int? SensitivityCount = null)
{
    public static readonly RiskIndexComposeOptions Full = new();

    /// <summary>A replay: node values and composition only.</summary>
    public static readonly RiskIndexComposeOptions Replay = new(ClassDays: false, SensitivityCount: 0);
}

/// <summary>The asset-class tabs of the dashboard (S40 §5.5): <see cref="RiskIndexTabMap"/>, the rule the desktop shares.</summary>
public static class RiskIndexTabGroups
{
    public static readonly RiskIndexTabGroup[] All = RiskIndexTabMap.All;

    public static RiskIndexTabGroup Of(RiskIndexAssetClass assetClass) => RiskIndexTabMap.Of(assetClass);

    public static IReadOnlyList<RiskIndexCategory> Categories(RiskIndexTabGroup tab) => RiskIndexTabMap.Categories(tab);
}

/// <summary>The index's service graph (S40 §9.2), called by the API and the job host when they wire the index in.</summary>
public static class RiskIndexServiceRegistration
{
    public static IServiceCollection AddRiskIndex(this IServiceCollection services)
    {
        services.AddTransient<IRiskIndexDayCalculator, RiskIndexDayCalculator>();

        // T247 (S40 §9.3, §9.6): the nightly snapshot and the retention pass; their jobs are thin wrappers in the job host.
        services.AddTransient<IRiskIndexSnapshotService, RiskIndexSnapshotService>();
        services.AddTransient<IRiskIndexRetentionService, RiskIndexRetentionService>();

        // T249 (S40 §8.1–§8.3): the read layer, its scope- and permission-keyed cache and the derived-computation budget —
        // both process-wide, so the limits hold across requests.
        services.AddSingleton<Read.IRiskIndexReadCache>(sp =>
            new Read.RiskIndexReadCache(sp.GetService<Security.IEntityHierarchy>()));
        services.AddSingleton<Read.IRiskIndexDerivedBudget>(_ => new Read.RiskIndexDerivedBudget());
        services.AddTransient<Read.IRiskIndexReadService, Read.RiskIndexReadService>();

        // T250 (S40 §8.4, §9.6): the profile lifecycle the API serves and the preview the job host computes.
        services.AddTransient<Profiles.IRiskIndexProfilesService, Profiles.RiskIndexProfilesService>();
        services.AddTransient<Profiles.IRiskIndexPreviewService, Profiles.RiskIndexPreviewService>();

        // M53 review (S40 §11.3): who reads the committee's minutes attached to a profile, asked by the files authorizer.
        services.AddTransient<Interfaces.IApprovalEvidenceReadRule, Profiles.RiskIndexEvidenceReadRule>();
        return services;
    }
}
