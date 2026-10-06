using DAL.Enums;

namespace Tools.Risks;

/// <summary>
/// Which entity definitions sit at which level of the linkage chain, and which properties are the
/// "serves" edges between them (Stage 9.1, S41 §4.2). Code constants, not configuration: the chain
/// is the methodology's, and an installation that renamed a property here would silently disconnect
/// its own coverage metric.
///
/// <c>RiskChainServiceInMemoryTest.TestY1_RiskChainSchemaConfigurationMatchesTheEntitiesConfiguration</c> holds every name below against the loaded
/// <c>EntitiesConfiguration.yaml</c>, so a rename on either side fails a test instead of a report.
/// </summary>
public static class RiskChainSchema
{
    public const string ObjectiveDefinition = "strategicObjective";
    public const string ProcessDefinition = "businessProcess";

    /// <summary>T242 (M52) introduces it; mapped already so it is a chain node the day it exists.</summary>
    public const string ActivityDefinition = "activity";

    public const string ItServiceDefinition = "itService";
    public const string DataDefinition = "organizationData";
    public const string DataGroupDefinition = "organizationDataGroup";
    public const string ApplicationDefinition = "application";
    public const string ApplicationModuleDefinition = "applicationModule";

    /// <summary>The <c>TargetType</c> of a host link: hosts are rows of <c>hosts</c>, not entities.</summary>
    public const string HostTargetType = "host";

    public const string NameProperty = "name";
    public const string CriticalityProperty = "criticality";
    public const string IsActiveProperty = "isActive";

    /// <summary>A process is critical when its declared criticality is at least this (S41 §11, D6).</summary>
    public const int CriticalThreshold = 4;

    public const int MinCriticality = 1;
    public const int MaxCriticality = 5;

    private static readonly Dictionary<string, RiskChainLevel> Levels = new(StringComparer.Ordinal)
    {
        [ObjectiveDefinition] = RiskChainLevel.Objective,
        [ProcessDefinition] = RiskChainLevel.Process,
        [ActivityDefinition] = RiskChainLevel.Process,
        [ItServiceDefinition] = RiskChainLevel.ItService,
        [DataDefinition] = RiskChainLevel.Data,
        [DataGroupDefinition] = RiskChainLevel.Data,
        [ApplicationDefinition] = RiskChainLevel.Asset,
        [ApplicationModuleDefinition] = RiskChainLevel.Asset
    };

    /// <summary>Every definition that is a node of the chain. Units, people, teams and classification
    /// levels are not: a unit is scope (<c>risks.entity_id</c>), not identification.</summary>
    public static IReadOnlyCollection<string> ChainDefinitions => Levels.Keys;

    /// <summary>The level an entity of <paramref name="definitionName"/> sits at, or null when that
    /// definition is not part of the chain.</summary>
    public static RiskChainLevel? LevelOf(string? definitionName) =>
        definitionName is not null && Levels.TryGetValue(definitionName, out var level) ? level : null;

    /// <summary>
    /// The typed "serves" edges, from the lower node to the upper one. An edge whose referenced id
    /// does not exist, or exists with another type, is dropped by <see cref="RiskChainGraph"/>.
    /// </summary>
    public static IReadOnlyList<RiskChainEdge> Edges { get; } =
    [
        // process → objective
        new(ProcessDefinition, "strategicObjectives", ObjectiveDefinition, OwnerIsLower: true, Multiple: true),
        // service → process
        new(ItServiceDefinition, "processes", ProcessDefinition, OwnerIsLower: true, Multiple: true),
        // application → service
        new(ItServiceDefinition, "applications", ApplicationDefinition, OwnerIsLower: false, Multiple: true),
        // data → service
        new(ItServiceDefinition, "data", DataDefinition, OwnerIsLower: false, Multiple: true),
        // application → process (existing property)
        new(ProcessDefinition, "applications", ApplicationDefinition, OwnerIsLower: false, Multiple: true),
        // module → application (existing property)
        new(ApplicationModuleDefinition, "parentApplication", ApplicationDefinition, OwnerIsLower: true,
            Multiple: false),
        // activity → process, through entities.Parent (T242)
        new(ActivityDefinition, null, ProcessDefinition, OwnerIsLower: true, Multiple: false)
    ];

    /// <summary>The entity properties the chain reads, for loading only those rows.</summary>
    public static IReadOnlyCollection<string> ReadProperties { get; } = Edges
        .Where(e => e.Property is not null)
        .Select(e => e.Property!)
        .Append(NameProperty)
        .Append(CriticalityProperty)
        .Append(IsActiveProperty)
        .Distinct(StringComparer.Ordinal)
        .ToList();
}

/// <summary>
/// One "serves" edge type. <paramref name="Property"/> is the property of an
/// <paramref name="OwnerDefinition"/> entity whose values are ids of <paramref name="ReferencedDefinition"/>
/// entities; null means the owner's <c>entities.Parent</c>. <paramref name="OwnerIsLower"/> says which
/// end serves the other: <c>itService.processes</c> is owned by the lower node (the service serves the
/// process), <c>itService.applications</c> by the upper one (the application serves the service).
/// </summary>
public sealed record RiskChainEdge(
    string OwnerDefinition,
    string? Property,
    string ReferencedDefinition,
    bool OwnerIsLower,
    bool Multiple)
{
    public bool IsParentEdge => Property is null;
}
