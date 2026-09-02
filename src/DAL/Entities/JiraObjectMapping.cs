using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// "Objects of this Assets type become this kind of NetRisk record" (Track 4 milestone 4.6).
///
/// The operator names the correspondence rather than the importer inferring it: Assets is a free-form
/// CMDB, so an object type called <c>Server</c>, <c>VM</c>, <c>Compute</c> or <c>Servidor</c> is
/// whatever the customer called it, and guessing from the name would work in English and fail
/// everywhere else.
/// </summary>
public class JiraObjectMapping
{
    public int Id { get; set; }

    public int ConnectionId { get; set; }

    /// <summary>The Assets object-type id.</summary>
    public int ObjectTypeId { get; set; }

    /// <summary>Cached type name, also used to build the default AQL (<c>objectType = "Server"</c>).</summary>
    public string ObjectTypeName { get; set; } = null!;

    public JiraAssetTargetKind TargetKind { get; set; } = JiraAssetTargetKind.Host;

    /// <summary>
    /// Extra AQL ANDed onto the type filter — <c>Status = "In Production"</c>. Free text because AQL
    /// is the customer's query language over their own schema; it is sent to Jira and never
    /// interpolated into SQL.
    /// </summary>
    public string? AqlFilter { get; set; }

    public AssetMatchStrategy MatchStrategy { get; set; } = AssetMatchStrategy.ExternalIdThenIdentity;

    public bool Enabled { get; set; } = true;

    /// <summary>Create the NetRisk record when nothing matches.</summary>
    public bool CreateMissing { get; set; } = true;

    /// <summary>Overwrite the mapped fields on a record that already exists.</summary>
    public bool UpdateExisting { get; set; } = true;

    /// <summary>
    /// Retire a previously imported object that the AQL no longer returns. **Off by default**: a typo
    /// in the filter would otherwise retire the estate, and an import that quietly decommissions
    /// production is worse than one that leaves a stale row.
    /// </summary>
    public bool DeactivateMissing { get; set; }

    public DateTime? LastImportedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual IssueTrackerConnection? Connection { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual ICollection<JiraObjectAttributeMapping> AttributeMappings { get; set; }
        = new List<JiraObjectAttributeMapping>();
}
