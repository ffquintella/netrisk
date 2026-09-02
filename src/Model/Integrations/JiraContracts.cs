using DAL.Enums;

namespace Model.Integrations;

// Track 4 milestone 4.6 — the client-facing shapes for Jira Service Management and Assets.
//
// Every one of these is a *view*: no credential appears in any of them, for the same reason the 4.2
// connection view carries has-a-token flags rather than the token. A response type with nowhere to put
// a secret cannot leak one by accident.

/// <summary>A connection's Jira facet as the client sees it (4.6).</summary>
public class JiraConnectionSettingsView
{
    public int ConnectionId { get; set; }

    public JiraDeployment Deployment { get; set; } = JiraDeployment.Cloud;

    public bool JsmEnabled { get; set; }

    public int? ServiceDeskId { get; set; }

    public string? ServiceDeskName { get; set; }

    public string? RequestTypeFilter { get; set; }

    public bool ImportSlas { get; set; } = true;

    public bool SlaBreachNotifications { get; set; }

    public IssueLinkTargetKind DefaultLinkTargetKind { get; set; } = IssueLinkTargetKind.Finding;

    public DateTime? LastJsmSyncAt { get; set; }

    public bool AssetsEnabled { get; set; }

    /// <summary>Discovered, not typed. Read-only in the UI, because guessing it produces 404s.</summary>
    public string? AssetsWorkspaceId { get; set; }

    public int? AssetsSchemaId { get; set; }

    public string? AssetsSchemaName { get; set; }

    public DateTime? LastAssetsSyncAt { get; set; }

    public List<JiraQueueImportView> QueueImports { get; set; } = new();
}

/// <summary>One queue selected for import (4.6).</summary>
public class JiraQueueImportView
{
    public int Id { get; set; }

    public int ServiceDeskId { get; set; }

    public int QueueId { get; set; }

    public string? QueueName { get; set; }

    public bool Enabled { get; set; } = true;

    public IssueLinkTargetKind? LinkTargetKind { get; set; }

    public int MaxRequests { get; set; } = 500;
}

/// <summary>A service desk, as the picker needs it.</summary>
public class JiraServiceDeskView
{
    public int Id { get; set; }

    public string ProjectKey { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;
}

/// <summary>A request type within a service desk.</summary>
public class JiraRequestTypeView
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}

/// <summary>A queue as Jira reports it, with the issue count it advertises.</summary>
public class JiraQueueView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Jql { get; set; }

    public int? IssueCount { get; set; }
}

/// <summary>A mirrored service request (4.6).</summary>
public class JiraServiceRequestView
{
    public int Id { get; set; }

    public int ConnectionId { get; set; }

    public string IssueKey { get; set; } = string.Empty;

    public string? RequestTypeName { get; set; }

    public string? Summary { get; set; }

    public string? StatusName { get; set; }

    public string? StatusCategory { get; set; }

    public string? ReporterDisplayName { get; set; }

    public string? OrganizationName { get; set; }

    public string? PriorityName { get; set; }

    public string? AssigneeDisplayName { get; set; }

    public bool IsClosed { get; set; }

    public string? RequestUrl { get; set; }

    public DateTime? CreatedAtRemote { get; set; }

    public DateTime? UpdatedAtRemote { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    public string? SyncError { get; set; }

    public List<JiraRequestSlaView> Slas { get; set; } = new();

    /// <summary>
    /// True when any SLA cycle on this request breached. Precomputed so a grid can colour a row
    /// without every client re-deriving the same rule from the cycle list.
    /// </summary>
    public bool AnySlaBreached { get; set; }
}

/// <summary>One SLA cycle (4.6).</summary>
public class JiraRequestSlaView
{
    public int Id { get; set; }

    public string MetricName { get; set; } = string.Empty;

    public bool IsOngoing { get; set; }

    public bool Breached { get; set; }

    public bool Paused { get; set; }

    public long? GoalDurationMs { get; set; }

    public long? ElapsedMs { get; set; }

    /// <summary>Negative once the goal is passed, which is how Jira reports it.</summary>
    public long? RemainingMs { get; set; }

    public DateTime? CycleStartAt { get; set; }

    public DateTime? CycleStopAt { get; set; }
}

/// <summary>A Jira field offered by the mapping editor's picker (4.6).</summary>
public class JiraFieldView
{
    /// <summary><c>priority</c>, <c>labels</c>, <c>customfield_10012</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Jira's schema type — it decides the JSON shape a write has to take.</summary>
    public string? Type { get; set; }

    public bool IsCustom { get; set; }
}

/// <summary>One NetRisk-field → Jira-field mapping row (4.6).</summary>
public class JiraFieldMappingView
{
    public int Id { get; set; }

    public JiraFieldMappingDirection Direction { get; set; } = JiraFieldMappingDirection.Outbound;

    public string NetRiskField { get; set; } = string.Empty;

    public string JiraFieldId { get; set; } = string.Empty;

    public string? JiraFieldName { get; set; }

    public string? JiraFieldType { get; set; }

    public JiraAttributeTransform Transform { get; set; }

    public string? ConstantValue { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>An Assets object schema.</summary>
public class JiraObjectSchemaView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ObjectSchemaKey { get; set; }

    public int? ObjectCount { get; set; }
}

/// <summary>An Assets object type within a schema.</summary>
public class JiraObjectTypeView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? ParentObjectTypeId { get; set; }

    public int? ObjectCount { get; set; }
}

/// <summary>One attribute of an Assets object type, for the mapping editor's source picker.</summary>
public class JiraObjectTypeAttributeView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Assets' own type label — <c>Text</c>, <c>Boolean</c>, <c>Object</c>, <c>User</c>.</summary>
    public string? Type { get; set; }

    public bool IsLabel { get; set; }
}

/// <summary>An Assets object-type → NetRisk mapping, with its attribute rows (4.6).</summary>
public class JiraObjectMappingView
{
    public int Id { get; set; }

    public int ObjectTypeId { get; set; }

    public string ObjectTypeName { get; set; } = string.Empty;

    public JiraAssetTargetKind TargetKind { get; set; } = JiraAssetTargetKind.Host;

    public string? AqlFilter { get; set; }

    public AssetMatchStrategy MatchStrategy { get; set; }

    public bool Enabled { get; set; } = true;

    public bool CreateMissing { get; set; } = true;

    public bool UpdateExisting { get; set; } = true;

    public bool DeactivateMissing { get; set; }

    public DateTime? LastImportedAt { get; set; }

    public List<JiraObjectAttributeMappingView> AttributeMappings { get; set; } = new();
}

/// <summary>One attribute → field row (4.6).</summary>
public class JiraObjectAttributeMappingView
{
    public int Id { get; set; }

    public int? SourceAttributeId { get; set; }

    public string SourceAttributeName { get; set; } = string.Empty;

    public string TargetField { get; set; } = string.Empty;

    public JiraAttributeTransform Transform { get; set; }

    public bool IsIdentity { get; set; }

    public string? ConstantValue { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>An imported Assets object and what it produced (4.6).</summary>
public class JiraAssetObjectView
{
    public int Id { get; set; }

    public string ObjectId { get; set; } = string.Empty;

    public string? ObjectKey { get; set; }

    public string? ObjectTypeName { get; set; }

    public string? Label { get; set; }

    public string? MappedName { get; set; }

    public string? MappedOwner { get; set; }

    public string? MappedEnvironment { get; set; }

    public bool? MappedActive { get; set; }

    public JiraAssetTargetKind TargetKind { get; set; }

    public int? TargetHostId { get; set; }

    public int? TargetEntityId { get; set; }

    public string? MatchReason { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    public string? ImportError { get; set; }
}

/// <summary>
/// The outcome of one Assets import pass (4.6), or of a dry run.
///
/// Per-object failures are counted and described rather than aborting the pass, on the same reasoning
/// as 4.2's bulk issue creation: importing 39 of 40 servers beats importing none because one object
/// has an unparseable date.
/// </summary>
public class AssetImportResult
{
    /// <summary>True when nothing was written.</summary>
    public bool DryRun { get; set; }

    public int Examined { get; set; }

    public int Created { get; set; }

    public int Updated { get; set; }

    public int Unchanged { get; set; }

    public int Deactivated { get; set; }

    public int Errors { get; set; }

    public List<string> Messages { get; set; } = new();

    /// <summary>
    /// The first rows as they would be written. The preview an operator reads before trusting a
    /// mapping against a register of ten thousand objects.
    /// </summary>
    public List<JiraAssetObjectView> Sample { get; set; } = new();
}

/// <summary>The outcome of one Service Management mirror pass (4.6).</summary>
public class JsmSyncResult
{
    public int QueuesExamined { get; set; }

    public int RequestsExamined { get; set; }

    public int RequestsCreated { get; set; }

    public int RequestsUpdated { get; set; }

    public int SlaCyclesRecorded { get; set; }

    public int Breaches { get; set; }

    public int Errors { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// A NetRisk field a mapping may read, published by the server so the editor's picker cannot drift
/// from what the mapping engine actually understands (4.6).
/// </summary>
public class MappableFieldView
{
    public string Name { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Which Assets target kinds accept it. Empty means every kind.</summary>
    public List<JiraAssetTargetKind> AppliesTo { get; set; } = new();

    public string? Description { get; set; }
}
