namespace DAL.Enums;

/// <summary>
/// What an imported Jira Assets object becomes in NetRisk (Track 4 milestone 4.6), persisted in
/// <c>jira_object_mappings.target_kind</c>.
///
/// Assets is a free-form CMDB: an object type called "Server", "Virtual Machine", "Application" or
/// "Business Service" is whatever the customer named it, so the operator says which NetRisk shape it
/// maps onto rather than the importer guessing from the type name.
/// </summary>
public enum JiraAssetTargetKind
{
    /// <summary>
    /// A row in <c>hosts</c> — servers and machines. Reconciled against the existing inventory
    /// through the same asset-identity chain milestone 4.4.2 uses, so an Assets server that a scanner
    /// already found updates that host instead of becoming a second one.
    /// </summary>
    Host = 1,

    /// <summary>
    /// An <c>entities</c> row on the <c>application</c> definition. Written through the entities
    /// service so the definition's own validation applies.
    /// </summary>
    ApplicationEntity = 2
}
