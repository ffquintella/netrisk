using System;

namespace DAL.Entities;

/// <summary>
/// "This host runs that application" (M58, T291, S39 §3.2 CMDB-REL, §10.1): the asset layer of the linkage chain the
/// full-mode drill-down (T284) attributes hosts to applications with. <c>hosts.entity_id</c> is one scope entity and
/// <c>risk_chain_links</c> links risks, so nothing else held it. Written by the Jira Assets import with
/// <see cref="AssetContextSources"/> precedence (an import touches only its own <c>cmdb</c> rows, never a <c>manual</c>
/// one); filtered through its host like <see cref="HostExternalId"/>, and audited.
/// </summary>
public class HostApplicationLink
{
    public int Id { get; set; }

    public int HostId { get; set; }

    /// <summary>The <c>application</c> entity.</summary>
    public int EntityId { get; set; }

    /// <summary><see cref="AssetContextSources.Cmdb"/> or <see cref="AssetContextSources.Manual"/>.</summary>
    public string Source { get; set; } = AssetContextSources.Cmdb;

    /// <summary>The Jira connection that last reported the relation; null for a manual row.</summary>
    public int? ConnectionId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC. Set when the reporting connection changes.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// UTC. When an import last reported the relation: a <c>cmdb</c> row older than the CMDB-REL window W (30 days by
    /// default) no longer counts for the reader. Null for a manual row, which does not age.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    public virtual Host Host { get; set; } = null!;

    public virtual Entity Entity { get; set; } = null!;

    public virtual IssueTrackerConnection? Connection { get; set; }
}
