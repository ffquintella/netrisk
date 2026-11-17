using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using Model.Authentication;
using Model.DataCatalogue;

namespace GUIClient.Agents;

/// <summary>
/// The data classes an organisation may keep away from agents (T362, S55 risk table "data leaves the machine"). Each is a
/// term of the S52 catalogue of a data record (<c>organizationData</c>) or of the S46 security classification:
/// nothing here invents a classification, it reads the ones NetRisk already keeps.
/// </summary>
public enum AgentDataClass
{
    /// <summary>S52 <see cref="PersonalDataCategory.Personal"/> — personal data (LGPD art. 5º, I).</summary>
    PersonalData,

    /// <summary>S52 <see cref="PersonalDataCategory.SensitivePersonal"/> — sensitive personal data (art. 5º, II).</summary>
    SensitivePersonalData,

    /// <summary>S52 <c>involves_minors</c> — data of children or adolescents (art. 14).</summary>
    ChildrenData,

    /// <summary>S52 <c>large_volume</c> — a large volume of data (one of the three conditions of flag 5).</summary>
    LargeVolume,

    /// <summary>S52 <c>strategic_research</c> — strategic research (flag 5).</summary>
    StrategicResearch,

    /// <summary>S46 — classified at a <c>securityClassificationLevel</c> marked <c>sensitive</c>.</summary>
    SensitiveClassification
}

/// <summary>Names and sets of <see cref="AgentDataClass"/>.</summary>
public static class AgentDataClasses
{
    /// <summary>Every class. An item whose classification cannot be established is treated as holding all of them.</summary>
    public static IReadOnlySet<AgentDataClass> All { get; } = Enum.GetValues<AgentDataClass>().ToHashSet();

    public static IReadOnlySet<AgentDataClass> None { get; } = new HashSet<AgentDataClass>();

    /// <summary>
    /// What flag 5 (S46, widened by S52 D6) can mean: sensitive personal data, a large volume, strategic research, or a
    /// sensitive classification. The flag does not say which, so a flagged risk is taken to hold all four (fails closed).
    /// </summary>
    public static IReadOnlySet<AgentDataClass> SensitiveDataFlag { get; } = new HashSet<AgentDataClass>
    {
        AgentDataClass.SensitivePersonalData, AgentDataClass.LargeVolume, AgentDataClass.StrategicResearch,
        AgentDataClass.SensitiveClassification
    };

    private static readonly IReadOnlyDictionary<AgentDataClass, string> Names = new Dictionary<AgentDataClass, string>
    {
        [AgentDataClass.PersonalData] = "personal_data",
        [AgentDataClass.SensitivePersonalData] = "sensitive_personal_data",
        [AgentDataClass.ChildrenData] = "children_data",
        [AgentDataClass.LargeVolume] = "large_volume",
        [AgentDataClass.StrategicResearch] = "strategic_research",
        [AgentDataClass.SensitiveClassification] = "sensitive_classification"
    };

    /// <summary>The stored and documented name of a class.</summary>
    public static string NameOf(AgentDataClass dataClass) => Names[dataClass];

    /// <summary>The localization key of a class's label in the Agents window and the access dialog (all three .resx).</summary>
    public static string LabelKey(AgentDataClass dataClass) => "AgentDataClass" + dataClass;

    public static bool TryParse(string? name, out AgentDataClass dataClass)
    {
        foreach (var (key, value) in Names)
        {
            if (!string.Equals(value, name, StringComparison.Ordinal)) continue;
            dataClass = key;
            return true;
        }
        dataClass = default;
        return false;
    }
}

/// <summary>
/// Which data classes are withheld from agents (T362). Read on every call from the per-user encrypted settings store
/// (<see cref="SettingName"/>); absent means <see cref="Default"/>. Changed only from the Agents window by a user holding
/// <see cref="ChangePermission"/>. The store is a local file another process of the same OS user can rewrite (spec M64,
/// "same-user agent"), so a stored policy can only add to <see cref="Floor"/>, never go below it.
/// </summary>
public sealed class AgentExclusionPolicy
{
    public const string SettingName = "AgentExcludedDataClasses";

    /// <summary>
    /// The S52 permission of whoever catalogues personal data (the DPO's office), or the Admin role claim, and never the
    /// third line — the same audience that may write the data catalogue (S52 §7).
    /// </summary>
    public static AgentToolPermission ChangePermission { get; } =
        AgentToolPermission.Granted("data_catalogue_manage").And(AgentToolPermission.NotThirdLine);

    private AgentExclusionPolicy(IEnumerable<AgentDataClass> excluded) => Excluded = excluded.ToHashSet();

    /// <summary>
    /// The organisation-wide minimum (T359 review): sensitive personal data and children's data are withheld whatever the
    /// per-desktop setting says. A constant — NetRisk has no server-side DPO setting for it yet (follow-up in the spec).
    /// </summary>
    public static IReadOnlySet<AgentDataClass> Floor { get; } = new HashSet<AgentDataClass>
    {
        AgentDataClass.SensitivePersonalData, AgentDataClass.ChildrenData
    };

    public IReadOnlySet<AgentDataClass> Excluded { get; }

    /// <summary>With nothing excluded nothing is classified and nothing withheld — not even an unknown classification.</summary>
    public bool IsActive => Excluded.Count > 0;

    /// <summary>
    /// The default: sensitive personal data and children's data (the S52 categories the LGPD gives special treatment,
    /// art. 11 and art. 14) and anything at a security classification level marked sensitive (S46).
    /// </summary>
    public static AgentExclusionPolicy Default { get; } = new(new[]
    {
        AgentDataClass.SensitivePersonalData, AgentDataClass.ChildrenData, AgentDataClass.SensitiveClassification
    });

    /// <summary>Every class: what a stored value that cannot be read means (fails closed).</summary>
    public static AgentExclusionPolicy All { get; } = new(AgentDataClasses.All);

    /// <summary>Nothing excluded, below the floor: for tests that are not about T362 only — no stored value yields it.</summary>
    internal static AgentExclusionPolicy None { get; } = new(Array.Empty<AgentDataClass>());

    /// <summary>A policy the user chose: always the <see cref="Floor"/> plus <paramref name="excluded"/>.</summary>
    public static AgentExclusionPolicy Of(IEnumerable<AgentDataClass> excluded) => new(excluded.Concat(Floor));

    /// <summary>Whether an item holding <paramref name="classes"/> is withheld.</summary>
    public bool Excludes(IReadOnlySet<AgentDataClass> classes) => IsActive && classes.Overlaps(Excluded);

    /// <summary>
    /// The stored value: class names separated by commas, added to the <see cref="Floor"/>; only the empty string means
    /// "nothing beyond the floor". Absent (null) is the default. Anything else that does not parse — an unknown name, an
    /// empty entry such as <c>","</c> — makes the whole value unreadable, which excludes every class.
    /// </summary>
    public static AgentExclusionPolicy Parse(string? stored)
    {
        if (stored is null) return Default;
        if (stored.Length == 0) return Of(Array.Empty<AgentDataClass>());
        var classes = new HashSet<AgentDataClass>();
        foreach (var name in stored.Split(','))
        {
            if (!AgentDataClasses.TryParse(name.Trim(), out var dataClass)) return All;
            classes.Add(dataClass);
        }
        return Of(classes);
    }

    public string Serialize() =>
        string.Join(",", Excluded.OrderBy(c => c).Select(AgentDataClasses.NameOf));

    public override string ToString() => IsActive ? Serialize() : "(none)";
}

/// <summary>
/// What an item names that carries a classification: entities, hosts (through their entity), risks (their entity, their
/// chain — where the GUI's "Entity" association lands, S41 §5.4 — and flag 5) and an assessment (its entity).
/// </summary>
public sealed record AgentItemLinks(IReadOnlyList<int> Entities, IReadOnlyList<int> Hosts, IReadOnlyList<int>? Risks = null,
    int? Assessment = null)
{
    public static AgentItemLinks Nothing { get; } = new(Array.Empty<int>(), Array.Empty<int>());

    public IReadOnlyList<int> RiskIds => Risks ?? Array.Empty<int>();

    private static IReadOnlyList<int> Ids(params int?[] ids) =>
        ids.Where(id => id is > 0).Select(id => id!.Value).Distinct().ToArray();

    public static AgentItemLinks ForEntity(int? id) => new(Ids(id), Array.Empty<int>());

    /// <summary>
    /// A risk: its <c>EntityId</c> column and its chain. The GUI never sets <c>EntityId</c>: its "Entity" field writes
    /// <c>risk_to_entity</c>, which the server mirrors into the chain as a Legacy link (T359 review, blocker 1).
    /// </summary>
    public static AgentItemLinks ForRisk(Risk r) => new(Ids(r.EntityId), Array.Empty<int>(), new[] { r.Id });

    /// <summary>A finding: its application, its host's entity, and every risk it is associated with (and theirs).</summary>
    public static AgentItemLinks ForFinding(Vulnerability v) =>
        new(Ids(new[] { v.EntityId }.Concat((v.Risks ?? Array.Empty<Risk>()).Select(r => r.EntityId)).ToArray()),
            Ids(v.HostId), (v.Risks ?? Array.Empty<Risk>()).Select(r => r.Id).Distinct().ToArray());

    public static AgentItemLinks ForIncident(Incident i) =>
        new(Ids(i.EntityId, i.ImpactedEntityId, i.ReportEntityId), Array.Empty<int>());

    public static AgentItemLinks ForHost(Host h) => new(Ids(h.EntityId), Array.Empty<int>());

    public static AgentItemLinks ForAssessment(int assessmentId) =>
        new(Array.Empty<int>(), Array.Empty<int>(), null, assessmentId);

    /// <summary>A run: the entity or host it assessed, and its assessment's own entity.</summary>
    public static AgentItemLinks ForRun(AssessmentRun r) => new(Ids(r.EntityId), Ids(r.HostId), null, r.AssessmentId);
}

/// <summary>What a risk's chain points at: entities and hosts; <see cref="Redacted"/> when a host link was hidden.</summary>
public sealed record AgentRiskChainTargets(IReadOnlyList<int> Entities, IReadOnlyList<int> Hosts, bool Redacted)
{
    public static AgentRiskChainTargets Of(Model.Risks.Chain.RiskChainDto chain)
    {
        var links = (chain.Levels ?? new()).SelectMany(l => l.Links ?? new()).ToList();
        return new AgentRiskChainTargets(
            links.Where(l => l.EntityId is > 0).Select(l => l.EntityId!.Value).Distinct().ToArray(),
            links.Where(l => l.HostId is > 0).Select(l => l.HostId!.Value).Distinct().ToArray(),
            links.Any(l => l.IsRedacted || (l.EntityId is null && l.HostId is null)));
    }
}

/// <summary>
/// Where the classification comes from: the API, with the user's session, through the same ClientServices the GUI uses.
/// Every method throws when it cannot answer; the scope reads a failure as "unknown", which withholds (fails closed).
/// </summary>
public interface IAgentClassificationSource
{
    Task<IReadOnlyList<Entity>> EntitiesAsync(CancellationToken cancellationToken);

    /// <summary>The S52 catalogue of every data record (<c>GET /DataCatalogue/Records</c>).</summary>
    Task<IReadOnlyList<DataRecordSummaryDto>> DataRecordsAsync(CancellationToken cancellationToken);

    /// <summary>The ids of the risks carrying flag 5, declared or derived (<c>GET /RiskFlags/Flagged?flag=5</c>).</summary>
    Task<IReadOnlyCollection<int>> SensitiveDataRiskIdsAsync(CancellationToken cancellationToken);

    /// <summary>What one risk's chain links to (<c>GET /RiskChain/Risks/{id}</c>), the GUI's "Entity" field included.</summary>
    Task<AgentRiskChainTargets> RiskChainAsync(int riskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Host>> HostsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<Assessment>> AssessmentsAsync(CancellationToken cancellationToken);
}

/// <summary>The production source: the GUI's own ClientServices instances.</summary>
public sealed class ClientServicesClassificationSource : IAgentClassificationSource
{
    private readonly IEntitiesService _entities;
    private readonly IDataCatalogueService _catalogue;
    private readonly IRiskFlagsService _flags;
    private readonly IHostsService _hosts;
    private readonly IAssessmentsService _assessments;
    private readonly IRiskChainService _chains;

    public ClientServicesClassificationSource(IEntitiesService entities, IDataCatalogueService catalogue,
        IRiskFlagsService flags, IHostsService hosts, IAssessmentsService assessments, IRiskChainService chains)
    {
        _chains = chains;
        _entities = entities;
        _catalogue = catalogue;
        _flags = flags;
        _hosts = hosts;
        _assessments = assessments;
    }

    public async Task<IReadOnlyList<Entity>> EntitiesAsync(CancellationToken cancellationToken) =>
        await _entities.GetAllFromServerAsync() ?? throw new InvalidOperationException("no entity list");

    public async Task<IReadOnlyList<DataRecordSummaryDto>> DataRecordsAsync(CancellationToken cancellationToken) =>
        await _catalogue.GetRecordsAsync() ?? throw new InvalidOperationException("no data catalogue");

    public async Task<IReadOnlyCollection<int>> SensitiveDataRiskIdsAsync(CancellationToken cancellationToken) =>
        ((await _flags.GetFlaggedAsync(RiskFlagCode.SensitiveData) ?? throw new InvalidOperationException("no flags"))
            .Where(f => f.Flags.Contains(RiskFlagCode.SensitiveData)).Select(f => f.RiskId).ToHashSet());

    public async Task<IReadOnlyList<Host>> HostsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return Array.Empty<Host>();
        // The filtered list by id, as the findings screen reads the hosts of its page: always the API, never a cache.
        var filter = "id==" + string.Join("|", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        var (items, _) = await _hosts.GetFilteredAsync(ids.Count, 1, filter, null);
        return items ?? throw new InvalidOperationException("no hosts");
    }

    public async Task<IReadOnlyList<Assessment>> AssessmentsAsync(CancellationToken cancellationToken) =>
        await _assessments.GetAssessmentsAsync() ?? throw new InvalidOperationException("no assessments");

    public async Task<AgentRiskChainTargets> RiskChainAsync(int riskId, CancellationToken cancellationToken) =>
        AgentRiskChainTargets.Of(await _chains.GetRiskChainAsync(riskId) ?? throw new InvalidOperationException("no chain"));
}

/// <summary>A source that answers nothing: every classified link reads as unknown.</summary>
public sealed class UnavailableClassificationSource : IAgentClassificationSource
{
    public static UnavailableClassificationSource Instance { get; } = new();

    private static Exception Unavailable() => new InvalidOperationException("no classification source");

    public Task<IReadOnlyList<Entity>> EntitiesAsync(CancellationToken cancellationToken) => throw Unavailable();
    public Task<IReadOnlyList<DataRecordSummaryDto>> DataRecordsAsync(CancellationToken cancellationToken) => throw Unavailable();
    public Task<IReadOnlyCollection<int>> SensitiveDataRiskIdsAsync(CancellationToken cancellationToken) => throw Unavailable();
    public Task<IReadOnlyList<Host>> HostsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken) => throw Unavailable();
    public Task<IReadOnlyList<Assessment>> AssessmentsAsync(CancellationToken cancellationToken) => throw Unavailable();
    public Task<AgentRiskChainTargets> RiskChainAsync(int riskId, CancellationToken cancellationToken) => throw Unavailable();
}

/// <summary>What the dispatcher hands every call: where the classification comes from and the policy, read per call.</summary>
public sealed class AgentClassificationOptions
{
    public AgentClassificationOptions(IAgentClassificationSource source, Func<AgentExclusionPolicy> policy)
    {
        Source = source;
        Policy = policy;
    }

    public IAgentClassificationSource Source { get; }
    public Func<AgentExclusionPolicy> Policy { get; }

    /// <summary>
    /// What a dispatcher composed without a classification gets: the default policy over a source that answers nothing,
    /// so every item that names an entity, a host, a risk or an assessment is withheld. Forgetting to wire the source
    /// therefore withholds rather than leaks.
    /// </summary>
    public static AgentClassificationOptions Unconfigured { get; } =
        new(UnavailableClassificationSource.Instance, () => AgentExclusionPolicy.Default);

    /// <summary>Nothing excluded — the policy a user with the change permission may choose; used by tests not about T362.</summary>
    internal static AgentClassificationOptions Off { get; } =
        new(UnavailableClassificationSource.Instance, () => AgentExclusionPolicy.None);
}

/// <summary>
/// The classes of each entity (pure): its own — the S46 classification level it points at, and for a data record the S52
/// catalogue — plus every ancestor's, so whatever sits under a classified node is classified with it. Anything that cannot
/// be established reads as <see cref="AgentDataClasses.All"/>: S52 D4, "absent is never compliant".
/// </summary>
public static class AgentEntityClassifier
{
    public const string DataRecordDefinition = "organizationData";
    public const string LevelDefinition = "securityClassificationLevel";
    public const string ClassificationProperty = "securityClassification";
    public const string SensitiveProperty = "sensitive";

    /// <param name="records">The catalogue; null when it could not be read (every data record is then unknown).</param>
    public static IReadOnlyDictionary<int, IReadOnlySet<AgentDataClass>> Classify(IReadOnlyList<Entity> entities,
        IReadOnlyList<DataRecordSummaryDto>? records)
    {
        var byId = new Dictionary<int, Entity>();
        foreach (var entity in entities) byId[entity.Id] = entity;
        // Duplicate rows for one record (not expected: the table is unique per node) are united, never one picked.
        var catalogue = records?.GroupBy(r => r.EntityId).ToDictionary(g => g.Key, g => (IReadOnlyList<DataRecordSummaryDto>)g.ToList());

        var own = byId.ToDictionary(p => p.Key, p => Own(p.Value, byId, catalogue, records is not null));
        var effective = new Dictionary<int, IReadOnlySet<AgentDataClass>>();
        foreach (var id in byId.Keys) effective[id] = Effective(id, byId, own, effective);
        return effective;
    }

    private static IReadOnlySet<AgentDataClass> Effective(int id, IReadOnlyDictionary<int, Entity> byId,
        IReadOnlyDictionary<int, HashSet<AgentDataClass>> own, Dictionary<int, IReadOnlySet<AgentDataClass>> effective)
    {
        if (effective.TryGetValue(id, out var known)) return known;
        var classes = new HashSet<AgentDataClass>();
        var seen = new HashSet<int>();
        int? current = id;
        while (current is { } node)
        {
            if (!seen.Add(node) || !byId.TryGetValue(node, out var entity))
                return AgentDataClasses.All; // a cycle, or a parent that is not on the list
            if (node != id && effective.TryGetValue(node, out var ancestors))
            {
                classes.UnionWith(ancestors);
                break;
            }
            classes.UnionWith(own[node]);
            current = entity.Parent;
        }
        return classes;
    }

    private static HashSet<AgentDataClass> Own(Entity entity, IReadOnlyDictionary<int, Entity> byId,
        IReadOnlyDictionary<int, IReadOnlyList<DataRecordSummaryDto>>? catalogue, bool catalogueRead)
    {
        var classes = new HashSet<AgentDataClass>();
        foreach (var property in (entity.EntitiesProperties ?? Array.Empty<EntitiesProperty>())
                 .Where(p => p.Type == ClassificationProperty))
        {
            var value = property.Value?.Trim();
            if (string.IsNullOrEmpty(value)) continue; // a nullable property saved empty: not classified (S46)
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var levelId) ||
                !byId.TryGetValue(levelId, out var level) || level.DefinitionName != LevelDefinition)
                return AgentDataClasses.All.ToHashSet();
            switch (SensitiveMark(level))
            {
                case true: classes.Add(AgentDataClass.SensitiveClassification); break;
                case null: return AgentDataClasses.All.ToHashSet();
            }
        }

        if (entity.DefinitionName != DataRecordDefinition) return classes;
        if (!catalogueRead || catalogue is null || !catalogue.TryGetValue(entity.Id, out var rows))
            return AgentDataClasses.All.ToHashSet(); // not catalogued, or the catalogue unreadable (S52 D4)
        foreach (var record in rows)
        {
            if (Record(record) is not { } recordClasses) return AgentDataClasses.All.ToHashSet();
            classes.UnionWith(recordClasses);
        }
        return classes;
    }

    /// <summary>One catalogue row's classes; null when the row says nothing usable (not catalogued, category undeclared).</summary>
    private static HashSet<AgentDataClass>? Record(DataRecordSummaryDto record)
    {
        if (!record.Catalogued || record.PersonalData is null) return null;
        var classes = new HashSet<AgentDataClass>();
        switch (record.PersonalData)
        {
            case PersonalDataCategory.Personal: classes.Add(AgentDataClass.PersonalData); break;
            case PersonalDataCategory.SensitivePersonal: classes.Add(AgentDataClass.SensitivePersonalData); break;
            case PersonalDataCategory.NotPersonal or PersonalDataCategory.Anonymised: break;
            default: return null;
        }
        // Undeclared (or a server too old to say) is taken as declared: the mark that cannot be read is assumed set.
        if (record.InvolvesMinors is not false) classes.Add(AgentDataClass.ChildrenData);
        if (record.LargeVolume is not false) classes.Add(AgentDataClass.LargeVolume);
        if (record.StrategicResearch is not false) classes.Add(AgentDataClass.StrategicResearch);
        return classes;
    }

    /// <summary>The level's <c>sensitive</c> mark: absent or blank is false (S46, the property's default), unreadable null.</summary>
    private static bool? SensitiveMark(Entity level)
    {
        var values = (level.EntitiesProperties ?? Array.Empty<EntitiesProperty>())
            .Where(p => p.Type == SensitiveProperty).Select(p => p.Value?.Trim()).ToList();
        var sensitive = false;
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value)) continue;
            if (!bool.TryParse(value, out var mark)) return null;
            sensitive |= mark;
        }
        return sensitive;
    }
}

/// <summary>
/// One call's view of the classification (T362): loads what it needs once, lazily, through the source, and answers
/// whether an item is withheld. Withheld items are counted in the call's <see cref="AgentWithholding"/>; a single item
/// asked for by id is refused with <see cref="AgentToolErrors.Withheld"/>. Every failure to classify withholds.
/// </summary>
public sealed class AgentClassificationScope
{
    private readonly IAgentClassificationSource _source;
    private readonly AgentWithholding _withheld;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyDictionary<int, IReadOnlySet<AgentDataClass>>? _entities;
    private bool _entitiesUnavailable;
    private IReadOnlyCollection<int>? _flaggedRisks;
    private bool _flagsUnavailable;
    private readonly Dictionary<int, int?> _hostEntities = new();
    private readonly HashSet<int> _hostsUnavailable = new();
    private IReadOnlyDictionary<int, int?>? _assessmentEntities;
    private bool _assessmentsUnavailable;
    private readonly Dictionary<int, AgentRiskChainTargets?> _riskChains = new();

    /// <summary>How many risk chains one call reads at the same time (a search reads one per risk).</summary>
    public const int ParallelChainReads = 8;

    public AgentClassificationScope(AgentClassificationOptions options, AgentWithholding withheld)
    {
        _source = options.Source;
        _withheld = withheld;
        try
        {
            Policy = options.Policy();
        }
        catch (Exception)
        {
            Policy = AgentExclusionPolicy.All; // an unreadable policy withholds every class
        }
    }

    public AgentExclusionPolicy Policy { get; }

    public bool IsActive => Policy.IsActive;

    /// <summary>The classes <paramref name="links"/> reach; <see cref="AgentDataClasses.All"/> when any cannot be told.</summary>
    public async Task<IReadOnlySet<AgentDataClass>> ClassesOfAsync(AgentItemLinks links, CancellationToken cancellationToken = default)
    {
        var classes = new HashSet<AgentDataClass>();
        var entityIds = links.Entities.ToList();
        var hostIds = links.Hosts.ToList();
        if (links.RiskIds.Count > 0)
        {
            var flagged = await FlaggedRisksAsync(cancellationToken).ConfigureAwait(false);
            if (flagged is null) return AgentDataClasses.All;
            await LoadRiskChainsAsync(links.RiskIds, cancellationToken).ConfigureAwait(false);
            foreach (var riskId in links.RiskIds)
            {
                if (flagged.Contains(riskId)) classes.UnionWith(AgentDataClasses.SensitiveDataFlag);
                // The chain carries the GUI's "Entity" association (Legacy links) and the declared links; a chain that
                // cannot be read, or hides a host from this user, cannot be classified.
                if (!_riskChains.TryGetValue(riskId, out var chain) || chain is null || chain.Redacted)
                    return AgentDataClasses.All;
                entityIds.AddRange(chain.Entities);
                hostIds.AddRange(chain.Hosts);
            }
        }

        if (links.Assessment is { } assessmentId)
        {
            var assessments = await AssessmentEntitiesAsync(cancellationToken).ConfigureAwait(false);
            if (assessments is null || !assessments.TryGetValue(assessmentId, out var assessmentEntity)) return AgentDataClasses.All;
            if (assessmentEntity is > 0) entityIds.Add(assessmentEntity.Value);
        }
        if (hostIds.Count > 0)
        {
            await LoadHostsAsync(hostIds, cancellationToken).ConfigureAwait(false);
            foreach (var hostId in hostIds.Distinct())
            {
                if (!_hostEntities.TryGetValue(hostId, out var hostEntity)) return AgentDataClasses.All;
                if (hostEntity is > 0) entityIds.Add(hostEntity.Value);
            }
        }

        if (entityIds.Count > 0)
        {
            var entities = await EntityClassesAsync(cancellationToken).ConfigureAwait(false);
            if (entities is null) return AgentDataClasses.All;
            foreach (var id in entityIds)
            {
                if (!entities.TryGetValue(id, out var entityClasses)) return AgentDataClasses.All; // a dangling link
                classes.UnionWith(entityClasses);
            }
        }
        return classes;
    }

    /// <summary>Whether the item is withheld under the policy. Never loads anything when the policy excludes nothing.</summary>
    public async Task<bool> IsExcludedAsync(AgentItemLinks links, CancellationToken cancellationToken = default) =>
        IsActive && Policy.Excludes(await ClassesOfAsync(links, cancellationToken).ConfigureAwait(false));

    /// <summary>The items that are not withheld, in order; each withheld one is counted, never named.</summary>
    public async Task<List<T>> KeepAsync<T>(IEnumerable<T> items, Func<T, AgentItemLinks> links,
        CancellationToken cancellationToken = default)
    {
        var list = items as IList<T> ?? items.ToList();
        if (!IsActive) return list.ToList();

        // Every risk chain of the list first (a few at a time), then the hosts of the whole list in one read.
        var allLinks = list.Select(links).ToList();
        var riskIds = allLinks.SelectMany(l => l.RiskIds).Distinct().ToList();
        if (riskIds.Count > 0) await LoadRiskChainsAsync(riskIds, cancellationToken).ConfigureAwait(false);
        var hostIds = allLinks.SelectMany(l => l.Hosts)
            .Concat(riskIds.SelectMany(id => _riskChains.TryGetValue(id, out var c) && c is not null ? c.Hosts : Array.Empty<int>()))
            .Distinct().ToList();
        if (hostIds.Count > 0) await LoadHostsAsync(hostIds, cancellationToken).ConfigureAwait(false);

        var kept = new List<T>(list.Count);
        foreach (var item in list)
        {
            if (await IsExcludedAsync(links(item), cancellationToken).ConfigureAwait(false)) _withheld.Add(AgentWithheldKind.Classified);
            else kept.Add(item);
        }
        return kept;
    }

    /// <summary>Refuses an item asked for by id when it is withheld: no data, no hint of which class.</summary>
    public async Task EnsureVisibleAsync(AgentItemLinks links, bool edit = false, CancellationToken cancellationToken = default)
    {
        if (!await IsExcludedAsync(links, cancellationToken).ConfigureAwait(false)) return;
        _withheld.Add(AgentWithheldKind.Classified);
        throw AgentToolErrors.WithheldError(edit);
    }

    /// <summary>Whether entity <paramref name="id"/> is withheld (for references inside a returned item).</summary>
    public Task<bool> IsEntityExcludedAsync(int id, CancellationToken cancellationToken = default) =>
        IsExcludedAsync(AgentItemLinks.ForEntity(id), cancellationToken);

    private async Task<IReadOnlyDictionary<int, IReadOnlySet<AgentDataClass>>?> EntityClassesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_entities is not null || _entitiesUnavailable) return _entities;
            IReadOnlyList<Entity> entities;
            try
            {
                entities = await _source.EntitiesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _entitiesUnavailable = true;
                return null;
            }

            IReadOnlyList<DataRecordSummaryDto>? records = null;
            if (entities.Any(e => e.DefinitionName == AgentEntityClassifier.DataRecordDefinition))
            {
                try
                {
                    records = await _source.DataRecordsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    records = null; // a user who cannot read the catalogue: every data record is unknown
                }
            }
            _entities = AgentEntityClassifier.Classify(entities, records);
            return _entities;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyCollection<int>?> FlaggedRisksAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_flaggedRisks is not null || _flagsUnavailable) return _flaggedRisks;
            try
            {
                _flaggedRisks = await _source.SensitiveDataRiskIdsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _flagsUnavailable = true;
            }
            return _flaggedRisks;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<int, int?>?> AssessmentEntitiesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_assessmentEntities is not null || _assessmentsUnavailable) return _assessmentEntities;
            try
            {
                var assessments = await _source.AssessmentsAsync(cancellationToken).ConfigureAwait(false);
                _assessmentEntities = assessments.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First().EntityId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _assessmentsUnavailable = true;
            }
            return _assessmentEntities;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads the chains not read yet, <see cref="ParallelChainReads"/> at a time; a failed read is kept as unknown.</summary>
    private async Task LoadRiskChainsAsync(IEnumerable<int> ids, CancellationToken cancellationToken)
    {
        List<int> missing;
        lock (_riskChains) missing = ids.Distinct().Where(id => !_riskChains.ContainsKey(id)).ToList();
        if (missing.Count == 0) return;
        using var limit = new SemaphoreSlim(ParallelChainReads);
        await Task.WhenAll(missing.Select(async id =>
        {
            await limit.WaitAsync(cancellationToken).ConfigureAwait(false);
            AgentRiskChainTargets? chain;
            try
            {
                chain = await _source.RiskChainAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                chain = null; // unknown: the risk is withheld
            }
            finally
            {
                limit.Release();
            }
            lock (_riskChains) _riskChains[id] = chain;
        })).ConfigureAwait(false);
    }

    private async Task LoadHostsAsync(IEnumerable<int> ids, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var missing = ids.Where(id => !_hostEntities.ContainsKey(id) && !_hostsUnavailable.Contains(id)).Distinct().ToList();
            if (missing.Count == 0) return;
            try
            {
                foreach (var host in await _source.HostsAsync(missing, cancellationToken).ConfigureAwait(false))
                    if (missing.Contains(host.Id)) _hostEntities[host.Id] = host.EntityId;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // left out below: unknown
            }
            foreach (var id in missing.Where(id => !_hostEntities.ContainsKey(id))) _hostsUnavailable.Add(id);
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>Who may see and change the policy in the Agents window.</summary>
public static class AgentExclusionPolicyAccess
{
    public static bool CanChange(AuthenticatedUserInfo? user) => AgentExclusionPolicy.ChangePermission.IsHeldBy(user);
}
