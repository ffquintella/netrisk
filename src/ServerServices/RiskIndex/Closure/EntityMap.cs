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

    /// <summary>The M39 chain nodes the full mode drills into (S39 §2.2; M59, T284).</summary>
    public const string ItService = "itService";

    public const string Data = "organizationData";

    /// <summary>The EAV links of a process: its units (multi-valued) and its applications (S39 §10.1).</summary>
    public const string ProcessUnitsProperty = "organizationUnit";

    public const string ProcessApplicationsProperty = "applications";

    /// <summary>The EAV links of an IT service (M39, T145; <c>Tools.Risks.RiskChainSchema.Edges</c>): the processes it
    /// serves, the applications that implement it (<see cref="ProcessApplicationsProperty"/>, same key) and the data it handles.</summary>
    public const string ServiceProcessesProperty = "processes";

    public const string ServiceDataProperty = "data";

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
/// What the full mode reads beside the entity map (M59): the MTPD of M41's business impact analyses by entity (T285 — the
/// process criticality the BIA derives, <c>Tools.Continuity.ProcessCriticality</c>) and the data records the M49 catalogue
/// marks sensitive (Δ_dados, S39 §5.2: the flag 5 basis of S52 §4.8).
/// </summary>
public sealed record EntityChainFacts(IReadOnlyDictionary<int, int> MtpdMinutes, IReadOnlySet<int> SensitiveData)
{
    public static readonly EntityChainFacts None = new(new Dictionary<int, int>(), new HashSet<int>());
}

/// <summary>
/// The M39 chain nodes an application serves (S39 §5.2 "processos, aplicações e serviços que o objeto serve"; M59, T284): the
/// IT services that list it, the processes that list it or are served by those services, and the data those services
/// handle. Read in full mode only.
/// </summary>
public sealed record ServedChain(int Application, IReadOnlyList<int> Services, IReadOnlyList<int> Processes,
    IReadOnlyList<int> Data);

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

    // M39 chain (full mode, M59): the EAV links of IT services and their reverse indexes.
    private readonly Dictionary<int, List<int>> _serviceProcesses;
    private readonly Dictionary<int, List<int>> _serviceApplications;
    private readonly Dictionary<int, List<int>> _serviceData;
    private readonly Dictionary<int, List<int>> _servicesByProcess;
    private readonly Dictionary<int, List<int>> _servicesByApplication;
    private readonly Dictionary<int, List<int>> _processesByApplication;
    private readonly EntityChainFacts _chain;

    private EntityMap(EntityTree tree, Dictionary<int, string> definitions, Dictionary<int, List<int>> processUnits,
        Dictionary<int, List<int>> processApplications, Dictionary<int, int> criticality,
        Dictionary<int, bool> internetFacing, Dictionary<int, int> classification, Dictionary<int, string> environment,
        IReadOnlyList<int> invalidCriticality, Dictionary<int, List<int>> serviceProcesses,
        Dictionary<int, List<int>> serviceApplications, Dictionary<int, List<int>> serviceData, EntityChainFacts chain,
        bool fullMode)
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
        _serviceProcesses = serviceProcesses;
        _serviceApplications = serviceApplications;
        _serviceData = serviceData;
        _chain = chain;
        FullMode = fullMode;

        _processesByUnit = Reverse(processUnits);
        _servicesByProcess = Reverse(serviceProcesses);
        _servicesByApplication = Reverse(serviceApplications);
        _processesByApplication = Reverse(processApplications);
    }

    private static Dictionary<int, List<int>> Reverse(Dictionary<int, List<int>> links)
    {
        var reverse = new Dictionary<int, List<int>>();
        foreach (var (owner, targets) in links)
        foreach (var target in targets)
        {
            if (!reverse.TryGetValue(target, out var list)) reverse[target] = list = [];
            if (!list.Contains(owner)) list.Add(owner);
        }

        foreach (var list in reverse.Values) list.Sort();
        return reverse;
    }

    public EntityTree Tree { get; }

    /// <summary>
    /// The map of a full-mode day (M59): the closure follows the M39 chain below processes and services (T284), and a
    /// process's criticality is the one M41's BIA derives when it has one (T285). False reads exactly as the interim map.
    /// </summary>
    public bool FullMode { get; }

    /// <summary>Entities whose declared criticality is outside 1–5 and was therefore ignored (T242: the API accepts any integer).</summary>
    public IReadOnlyList<int> InvalidCriticality { get; }

    public IEnumerable<int> Ids => _definitions.Keys;

    /// <param name="chain">M41 and M49 facts; read only when <paramref name="fullMode"/> is set.</param>
    /// <param name="fullMode">Builds the full-mode map (M59): see <see cref="FullMode"/>.</param>
    public static EntityMap Build(IEnumerable<EntityMapRow> entities, IEnumerable<EntityPropertyRow> properties,
        EntityChainFacts? chain = null, bool fullMode = false)
    {
        var rows = entities.ToList();
        var tree = EntityTree.Build(rows.Select(r => new EntityTreeRow(r.Id, r.Parent)), 0);
        var definitions = rows.ToDictionary(r => r.Id, r => r.Definition);

        var units = new Dictionary<int, List<int>>();
        var applications = new Dictionary<int, List<int>>();
        var serviceProcesses = new Dictionary<int, List<int>>();
        var serviceApplications = new Dictionary<int, List<int>>();
        var serviceData = new Dictionary<int, List<int>>();
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
                case RiskIndexEntityTypes.ProcessApplicationsProperty when definition == RiskIndexEntityTypes.ItService:
                    AddIds(serviceApplications, p.EntityId, p.Value);
                    break;
                case RiskIndexEntityTypes.ServiceProcessesProperty when definition == RiskIndexEntityTypes.ItService:
                    AddIds(serviceProcesses, p.EntityId, p.Value);
                    break;
                case RiskIndexEntityTypes.ServiceDataProperty when definition == RiskIndexEntityTypes.ItService:
                    AddIds(serviceData, p.EntityId, p.Value);
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
            invalid.Distinct().OrderBy(i => i).ToList(), serviceProcesses, serviceApplications, serviceData,
            chain ?? EntityChainFacts.None, fullMode);
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

    /// <summary>
    /// The criticality 1–5 of an entity (S39 §5.2), or null when absent or out of range: the declared EAV value ("declared,
    /// not BIA"); in full mode a process with an M41 BIA reads the criticality its MTPD derives instead — the BIA wins over
    /// the declared property, as in M41's own coverage metric (S43 D4; M59, T285).
    /// </summary>
    public int? Criticality(int id) => CriticalityWithSource(id).Value;

    /// <summary>The criticality and where it came from: <c>bia</c>, <c>entity</c> (declared) or null.</summary>
    public (int? Value, string? Source) CriticalityWithSource(int id)
    {
        if (FullMode && DefinitionOf(id) == RiskIndexEntityTypes.BusinessProcess && _chain.MtpdMinutes.TryGetValue(id, out var mtpd))
            return (Tools.Continuity.ProcessCriticality.FromMtpd(mtpd), CriticalitySourceBia);
        return _criticality.TryGetValue(id, out var c) ? (c, CriticalitySourceEntity) : (null, null);
    }

    public const string CriticalitySourceBia = "bia";
    public const string CriticalitySourceEntity = "entity";

    /// <summary>A data record the M49 catalogue marks sensitive (full mode; S39 §5.2 Δ_dados).</summary>
    public bool IsSensitiveData(int id) => FullMode && _chain.SensitiveData.Contains(id);

    public IReadOnlyList<int> ServicesOfProcess(int process) => _servicesByProcess.GetValueOrDefault(process) ?? [];

    public IReadOnlyList<int> ServicesOfApplication(int application) => _servicesByApplication.GetValueOrDefault(application) ?? [];

    public IReadOnlyList<int> ProcessesOfService(int service) => _serviceProcesses.GetValueOrDefault(service) ?? [];

    public IReadOnlyList<int> ApplicationsOfService(int service) => _serviceApplications.GetValueOrDefault(service) ?? [];

    public IReadOnlyList<int> DataOfService(int service) => _serviceData.GetValueOrDefault(service) ?? [];

    /// <summary>
    /// What an application serves through the M39 chain (S39 §5.2; T284): the services listing it, the processes listing it
    /// or served by those services, and the data those services handle — only entities the map holds, in id order.
    /// </summary>
    public ServedChain Served(int application)
    {
        var services = ServicesOfApplication(application).Where(Contains).ToList();
        var processes = ProcessesOfApplication(application)
            .Concat(services.SelectMany(ProcessesOfService))
            .Where(id => DefinitionOf(id) == RiskIndexEntityTypes.BusinessProcess)
            .Distinct().Order().ToList();
        var data = services.SelectMany(DataOfService).Where(id => DefinitionOf(id) == RiskIndexEntityTypes.Data)
            .Distinct().Order().ToList();
        return new ServedChain(application, services, processes, data);
    }

    /// <summary>
    /// The M39 chain below a process or a service (T284): a process's serving services, and a service's applications and
    /// data. Activities and a process's own applications are already in the interim closure.
    /// </summary>
    private IEnumerable<int> ChainBelow(int id)
    {
        var definition = DefinitionOf(id);
        IEnumerable<int> services = definition switch
        {
            RiskIndexEntityTypes.BusinessProcess => ServicesOfProcess(id),
            RiskIndexEntityTypes.ItService => [id],
            _ => []
        };

        foreach (var service in services.Where(Contains))
        {
            if (service != id) yield return service;
            foreach (var application in ApplicationsOfService(service).Where(Contains)) yield return application;
            foreach (var data in DataOfService(service).Where(Contains)) yield return data;
        }
    }

    public bool? InternetFacing(int id) => _internetFacing.TryGetValue(id, out var b) ? b : null;

    public int? SecurityClassification(int id) => _classification.TryGetValue(id, out var c) ? c : null;

    public string? Environment(int id) => _environment.GetValueOrDefault(id);

    public IReadOnlyList<int> UnitsOfProcess(int process) => _processUnits.GetValueOrDefault(process) ?? [];

    public IReadOnlyList<int> ApplicationsOfProcess(int process) => _processApplications.GetValueOrDefault(process) ?? [];

    /// <summary>The processes that list <paramref name="application"/> in their <c>applications</c> property.</summary>
    public IEnumerable<int> ProcessesOfApplication(int application) =>
        _processesByApplication.GetValueOrDefault(application) ?? [];

    /// <summary>
    /// <c>fecho(n)</c> (S39 §10.1): n and its descendants; the processes whose <c>organizationUnit</c> names any of them,
    /// and those processes' descendants (activities); and the applications any process in the closure lists, with their
    /// descendants. Unknown <paramref name="node"/> closes over itself only. In full mode (M59, T284) the closure then
    /// follows the M39 chain down from every process and IT service in it — the services that serve the process, their
    /// applications and data — so a process holds what its services hold; those are EAV links, never an access grant.
    /// </summary>
    public EntityClosure Closure(int node)
    {
        var interim = InterimClosure(node);
        if (!FullMode) return interim;

        var members = new HashSet<int>(interim.Members);
        var tree = Tree.SelfAndDescendants(node);
        var eavOnly = interim.LinkedOnlyThroughEav;
        foreach (var member in interim.Members.Where(id => DefinitionOf(id) is RiskIndexEntityTypes.BusinessProcess
                     or RiskIndexEntityTypes.ItService).ToList())
        foreach (var below in ChainBelow(member))
        {
            if (!tree.Contains(below)) eavOnly = true;
            foreach (var descendant in Tree.SelfAndDescendants(below)) members.Add(descendant);
        }

        return new EntityClosure(node, members, eavOnly);
    }

    private EntityClosure InterimClosure(int node)
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
