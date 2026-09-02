using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One imported Jira Assets object, and the record of what NetRisk did with it
/// (Track 4 milestone 4.6).
///
/// Every object read gets a row, including the ones that did not resolve — with
/// <see cref="MatchReason"/> saying which rule matched and <see cref="ImportError"/> saying why it
/// did not. A register import you cannot audit is a register import nobody trusts: without this
/// table, "why is that server not in NetRisk" has no answer but re-running the import and watching.
/// </summary>
public class JiraAssetObject
{
    public int Id { get; set; }

    public int ConnectionId { get; set; }

    /// <summary>The Assets object id. Unique per connection, and the primary identity for matching.</summary>
    public string ObjectId { get; set; } = null!;

    /// <summary>The human key — <c>ITSM-88</c>.</summary>
    public string? ObjectKey { get; set; }

    public int? ObjectTypeId { get; set; }

    public string? ObjectTypeName { get; set; }

    /// <summary>Assets' own display label for the object.</summary>
    public string? Label { get; set; }

    // --- what the mapping produced ----------------------------------------------------------
    // Stored as well as applied, so the audit answers "what did NetRisk read" separately from
    // "what does the host say now" -- the second can have been edited by a person since.

    public string? MappedName { get; set; }

    public string? MappedOwner { get; set; }

    public string? MappedEnvironment { get; set; }

    public bool? MappedActive { get; set; }

    /// <summary>
    /// The object's attributes as Assets returned them. Text, rendered in a grid, never interpolated
    /// into SQL or into anything evaluated — it is third-party data and is treated as such.
    /// </summary>
    public string? AttributesJson { get; set; }

    public JiraAssetTargetKind TargetKind { get; set; }

    /// <summary>The host this became, when it became one.</summary>
    public int? TargetHostId { get; set; }

    /// <summary>The <c>application</c> entity this became, when it became one.</summary>
    public int? TargetEntityId { get; set; }

    /// <summary>Which rule matched — <c>external-id</c>, <c>mac</c>, <c>fqdn</c>, <c>created</c>.</summary>
    public string? MatchReason { get; set; }

    public DateTime? CreatedAtRemote { get; set; }

    public DateTime? UpdatedAtRemote { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Why this object produced nothing. Null once it imports cleanly.</summary>
    public string? ImportError { get; set; }

    public virtual IssueTrackerConnection? Connection { get; set; }

    public virtual Host? TargetHost { get; set; }

    public virtual Entity? TargetEntity { get; set; }
}
