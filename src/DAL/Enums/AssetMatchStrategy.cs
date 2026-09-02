namespace DAL.Enums;

/// <summary>
/// How an imported Assets object is matched against what NetRisk already has (Track 4 milestone
/// 4.6), persisted in <c>jira_object_mappings.match_strategy</c>.
/// </summary>
public enum AssetMatchStrategy
{
    /// <summary>
    /// The Assets object id, held in <c>hosts.external_id</c> with <c>external_provider =
    /// 'JiraAssets'</c>, then the asset-identity chain from 4.4.2 — MAC, FQDN, hostname, IP. The
    /// default, and the only strategy that survives a machine being renamed *and* re-addressed.
    /// </summary>
    ExternalIdThenIdentity = 0,

    /// <summary>
    /// The Assets object id only. For an estate where NetRisk holds several deliberately distinct
    /// rows for one physical machine and the identity chain would merge them.
    /// </summary>
    ExternalIdOnly = 1,

    /// <summary>
    /// The mapped name only, against <c>hosts.host_name</c> or the entity's <c>name</c>. For a CMDB
    /// whose object ids are not stable across its own migrations.
    /// </summary>
    NameOnly = 2
}
