using System.Globalization;
using DAL.Context;
using Model.Entities;

namespace ServerServices.RiskIndex.Closure;

/// <summary>The entity definitions the index reads (<c>src/API/EntitiesConfiguration.yaml</c> v2.7).</summary>
public static class RiskIndexEntityTypes
{
    public const string Organization = "organization";
    public const string OrganizationUnit = "organizationUnit";
    public const string SubOrganizationUnit = "subOrganizationUnit";
    public const string BusinessProcess = "businessProcess";
    public const string Activity = "activity";
    public const string Application = "application";

    /// <summary>The EAV links of a process: its units (multi-valued) and its applications (S39 §10.1).</summary>
    public const string ProcessUnitsProperty = "organizationUnit";

    public const string ProcessApplicationsProperty = "applications";

    /// <summary>The entity types with a pre-computed node every night (S39 §9.1; S40 §5.0 "oficial").</summary>
    public static readonly IReadOnlySet<string> PrecomputedNodeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        Organization, OrganizationUnit, SubOrganizationUnit, BusinessProcess, Application
    };

    /// <summary>The types that are targets of incidents and assessments in AME's inventory (S39 §2.1).</summary>
    public static readonly IReadOnlySet<string> TargetTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        Organization, OrganizationUnit, SubOrganizationUnit, BusinessProcess, Activity, Application
    };
}

/// <summary>One entity as the map needs it.</summary>
public readonly record struct EntityMapRow(int Id, string Definition, int? Parent);

/// <summary>One EAV property value (<c>entities_properties.type</c> is the key).</summary>
public readonly record struct EntityPropertyRow(int EntityId, string Key, string? Value);

/// <summary>A process linked to units through EAV that are not its parent in the tree (quality: "divergent link").</summary>
public sealed record DivergentLink(int ProcessId, int TreeParent, IReadOnlyList<int> EavUnits);

/// <summary>The resolved closure of one node (S39 §10.1).</summary>
public sealed record EntityClosure(int Node, IReadOnlySet<int> Members, bool LinkedOnlyThroughEav);

/// <summary>
/// The entity map as the index walks it (S39 §10.1, D-17): the tree (<c>entities.parent</c>, through the cycle-safe
/// <see cref="EntityTree"/>), the EAV links of processes, and the risk-context properties. Immutable; built once per day.
/// </summary>
public sealed class EntityMap
{
    private readonly Dictionary<int, string> _definitions;
    private readonly Dictionary<int, List<int>> _processUnits;
    private readonly Dictionary<int, List<int>> _processApplications;
    private readonly Dictionary<int, List<int>> _processesByUnit;
    private readonly Dictionary<int, int> _criticality;
    private readonly Dictionary<int, bool> _internetFacing;
    private readonly Dictionary<int, int> _classification;
    private readonly Dictionary<int, string> _environment;

    private EntityMap(EntityTree tree, Dictionary<int, string> definitions, Dictionary<int, List<int>> processUnits,
        Dictionary<int, List<int>> processApplications, Dictionary<int, int> criticality,
        Dictionary<int, bool> internetFacing, Dictionary<int, int> classification, Dictionary<int, string> environment,
        IReadOnlyList<int> invalidCriticality)
    {
        Tree = tree;
        _definitions = definitions;
        _processUnits = processUnits;
        _processApplications = processApplications;
        _criticality = criticality;
        _internetFacing = internetFacing;
        _classification = classification;
        _environment = environment;
        InvalidCriticality = invalidCriticality;

        _processesByUnit = new Dictionary<int, List<int>>();
        foreach (var (process, units) in processUnits)
        foreach (var unit in units)
        {
            if (!_processesByUnit.TryGetValue(unit, out var list)) _processesByUnit[unit] = list = [];
            list.Add(process);
        }
    }

    public EntityTree Tree { get; }

    /// <summary>Entities whose declared criticality is outside 1–5 and was therefore ignored (T242: the API accepts any integer).</summary>
    public IReadOnlyList<int> InvalidCriticality { get; }

    public IEnumerable<int> Ids => _definitions.Keys;

    public static EntityMap Build(IEnumerable<EntityMapRow> entities, IEnumerable<EntityPropertyRow> properties)
    {
        var rows = entities.ToList();
        var tree = EntityTree.Build(rows.Select(r => new EntityTreeRow(r.Id, r.Parent)), 0);
        var definitions = rows.ToDictionary(r => r.Id, r => r.Definition);

        var units = new Dictionary<int, List<int>>();
        var applications = new Dictionary<int, List<int>>();
        var criticality = new Dictionary<int, int>();
        var internet = new Dictionary<int, bool>();
        var classification = new Dictionary<int, int>();
        var environment = new Dictionary<int, string>();
        var invalid = new List<int>();

        foreach (var p in properties)
        {
            if (!definitions.TryGetValue(p.EntityId, out var definition)) continue;

            switch (p.Key)
            {
                case RiskIndexEntityTypes.ProcessUnitsProperty when definition == RiskIndexEntityTypes.BusinessProcess:
                    AddIds(units, p.EntityId, p.Value);
                    break;
                case RiskIndexEntityTypes.ProcessApplicationsProperty when definition == RiskIndexEntityTypes.BusinessProcess:
                    AddIds(applications, p.EntityId, p.Value);
                    break;
                case RiskContextProperties.Criticality:
                    if (int.TryParse(p.Value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
                    {
                        if (c is >= 1 and <= 5) criticality[p.EntityId] = c;
                        else invalid.Add(p.EntityId);
                    }

                    break;
                case RiskContextProperties.InternetFacing:
                    if (bool.TryParse(p.Value?.Trim(), out var b)) internet[p.EntityId] = b;
                    else if (p.Value?.Trim() == "1") internet[p.EntityId] = true;
                    else if (p.Value?.Trim() == "0") internet[p.EntityId] = false;
                    break;
                case RiskContextProperties.SecurityClassification:
                    if (int.TryParse(p.Value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                        classification[p.EntityId] = level;
                    break;
                case "environment":
                    if (!string.IsNullOrWhiteSpace(p.Value)) environment[p.EntityId] = p.Value.Trim();
                    break;
            }
        }

        return new EntityMap(tree, definitions, units, applications, criticality, internet, classification, environment,
            invalid.Distinct().OrderBy(i => i).ToList());
    }

    /// <summary>A multi-valued Definition property: one row per value, or several ids in one value.</summary>
    private static void AddIds(Dictionary<int, List<int>> map, int entity, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var token in value.Split([',', ';', ' ', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            if (!map.TryGetValue(entity, out var list)) map[entity] = list = [];
            if (!list.Contains(id)) list.Add(id);
        }
    }

    public bool Contains(int id) => _definitions.ContainsKey(id);

    public string? DefinitionOf(int id) => _definitions.GetValueOrDefault(id);

    /// <summary>The declared criticality 1–5 (S39 §5.2 "declared, not BIA"), or null when absent or out of range.</summary>
    public int? Criticality(int id) => _criticality.TryGetValue(id, out var c) ? c : null;

    public bool? InternetFacing(int id) => _internetFacing.TryGetValue(id, out var b) ? b : null;

    public int? SecurityClassification(int id) => _classification.TryGetValue(id, out var c) ? c : null;

    public string? Environment(int id) => _environment.GetValueOrDefault(id);

    public IReadOnlyList<int> UnitsOfProcess(int process) => _processUnits.GetValueOrDefault(process) ?? [];

    public IReadOnlyList<int> ApplicationsOfProcess(int process) => _processApplications.GetValueOrDefault(process) ?? [];

    /// <summary>The processes that list <paramref name="application"/> in their <c>applications</c> property.</summary>
    public IEnumerable<int> ProcessesOfApplication(int application) =>
        _processApplications.Where(kv => kv.Value.Contains(application)).Select(kv => kv.Key);

    /// <summary>
    /// <c>fecho(n)</c> (S39 §10.1): n and its descendants; the processes whose <c>organizationUnit</c> names any of them,
    /// and those processes' descendants (activities); and the applications any process in the closure lists, with their
    /// descendants. Unknown <paramref name="node"/> closes over itself only.
    /// </summary>
    public EntityClosure Closure(int node)
    {
        var tree = Tree.SelfAndDescendants(node);
        var members = new HashSet<int>(tree);

        var processes = new HashSet<int>(tree.Where(id => DefinitionOf(id) == RiskIndexEntityTypes.BusinessProcess));
        var eavOnly = false;
        foreach (var id in tree)
        foreach (var process in _processesByUnit.GetValueOrDefault(id) ?? [])
        {
            if (!processes.Add(process)) continue;
            if (!tree.Contains(process)) eavOnly = true;
            foreach (var descendant in Tree.SelfAndDescendants(process)) members.Add(descendant);
        }

        foreach (var process in processes)
        foreach (var application in ApplicationsOfProcess(process))
        {
            if (!tree.Contains(application)) eavOnly = true;
            foreach (var descendant in Tree.SelfAndDescendants(application)) members.Add(descendant);
        }

        return new EntityClosure(node, members, eavOnly);
    }

    /// <summary>Processes whose tree parent is a unit absent from their <c>organizationUnit</c> list (S39 §10.1).</summary>
    public IReadOnlyList<DivergentLink> DivergentLinks() =>
        _processUnits
            .Where(kv => Tree.ParentOf(kv.Key) is { } parent && kv.Value.Count > 0 && !kv.Value.Contains(parent) &&
                         DefinitionOf(parent) is RiskIndexEntityTypes.OrganizationUnit or RiskIndexEntityTypes.SubOrganizationUnit)
            .Select(kv => new DivergentLink(kv.Key, Tree.ParentOf(kv.Key)!.Value, kv.Value.OrderBy(i => i).ToList()))
            .OrderBy(d => d.ProcessId)
            .ToList();

    /// <summary>Applications at the root that no process lists: they enter no unit's closure (S39 §8.6, §10.1).</summary>
    public IReadOnlyList<int> OrphanApplications()
    {
        var listed = _processApplications.Values.SelectMany(v => v).ToHashSet();
        return _definitions
            .Where(kv => kv.Value == RiskIndexEntityTypes.Application && Tree.ParentOf(kv.Key) is null && !listed.Contains(kv.Key))
            .Select(kv => kv.Key).OrderBy(i => i).ToList();
    }
}
