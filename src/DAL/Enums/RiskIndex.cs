namespace DAL.Enums;

// The stored vocabularies of the Consolidated Risk Index (ICR, S39 §12.5; S40 §7.1, §8.3). Every value is explicit and
// only ever appended: the snapshot and object rows are history, kept for 400 and 1825 days, and a renumbered value would
// silently change what an old row means. S40 §8.3 places these under Model.RiskIndex; they live here because the DAL
// entities store them and Model references DAL, not the reverse (the convention of every other stored enum).

/// <summary>
/// A category of the index, or the index itself. Stored as <c>risk_index_snapshots.category</c> (0–6) and
/// <c>risk_index_object_days.category</c> (1–6: an object is scored per category, never on the headline).
/// </summary>
public enum RiskIndexCategory
{
    /// <summary>The headline (ICR), only on a node row.</summary>
    Icr = 0,

    /// <summary>REG — the register's scenarios.</summary>
    Reg = 1,

    /// <summary>EXP — technical exposure.</summary>
    Exp = 2,

    /// <summary>AME — threats and events.</summary>
    Ame = 3,

    /// <summary>CTL — controls and compliance.</summary>
    Ctl = 4,

    /// <summary>GOV — governance and treatment.</summary>
    Gov = 5,

    /// <summary>TER — third parties (weight 0 until M48).</summary>
    Ter = 6
}

/// <summary>
/// Which formulas a profile computes (S39 §15.1). Stored as <c>risk_index_profiles.mode</c> and copied onto every snapshot
/// row. Changing it needs a new profile version and is always a rebaseline (S39 §9.4, §12.4).
/// </summary>
public enum RiskIndexMode
{
    Interim = 1,
    Full = 2
}

/// <summary>
/// The lifecycle of a profile (S39 §13.1, §12.5). A profile is edited only as <see cref="Draft"/>; an
/// <see cref="Active"/> or <see cref="Retired"/> row is immutable except for the Active → Retired transition.
/// </summary>
public enum RiskIndexProfileStatus
{
    /// <summary>Being edited by its author (<c>risk_index_configure</c>).</summary>
    Draft = 1,

    /// <summary>Submitted with a justification and the sensitivity preview; awaiting the committee.</summary>
    PendingApproval = 2,

    /// <summary>Approved by someone other than the author, holding <c>risk_index_approve</c> through an approver role.</summary>
    Active = 3,

    /// <summary>Superseded by a newer active profile, or suspended without a successor.</summary>
    Retired = 4,

    /// <summary>Refused by the approver, with a reason.</summary>
    Rejected = 5
}

/// <summary>What a node row of <c>risk_index_snapshots</c> aggregates (S39 §9.1: the pre-computed nodes).</summary>
public enum RiskIndexScopeKind
{
    /// <summary>The root: the whole organization.</summary>
    Organization = 1,

    /// <summary>An entity of the map (organization unit, sub-unit, process, application), or the "Unassigned" node.</summary>
    Entity = 2,

    /// <summary>An asset class at the root (<see cref="RiskIndexAssetClass"/>).</summary>
    AssetClass = 3,

    /// <summary>A source of the S39 §3.2 catalogue at the root.</summary>
    Source = 4
}

/// <summary>What a row of <c>risk_index_object_days</c> scores (S39 §2.1).</summary>
public enum RiskIndexObjectKind
{
    /// <summary>An open risk (<c>risks.id</c>).</summary>
    Scenario = 1,

    /// <summary>A server, workstation or VM (<c>hosts.id</c>).</summary>
    Host = 2,

    /// <summary>An external domain — a SecurityScorecard connection.</summary>
    ExternalDomain = 3,

    /// <summary>An application with findings and no host (<c>entities.id</c>).</summary>
    Application = 4,

    /// <summary>A vendor asset — Vision One FQDN/IP, identity, cloud asset or local app.</summary>
    VendorAsset = 5,

    /// <summary>An entity targeted by incidents and assessments (<c>entities.id</c>).</summary>
    TargetEntity = 6,

    /// <summary>A Vision One tenant — its connection.</summary>
    Tenant = 7,

    /// <summary>A supplier of the third-party register (M48).</summary>
    Supplier = 8,

    /// <summary>The objects attributed to no entity, grouped for the "Unassigned" node.</summary>
    UnassignedGroup = 9
}

/// <summary>The state of an object in a category (S39 §8.1).</summary>
public enum RiskIndexObjectState
{
    /// <summary>Some factor has a current reading (<c>v &gt; 0</c>).</summary>
    Assessed = 1,

    /// <summary>Assessed with no signal: enters with R = 0.</summary>
    AssessedClean = 2,

    /// <summary>Assessed, but no factor fully current: the value slides up towards the prior.</summary>
    Stale = 3,

    /// <summary>In the inventory with an enabled source and no current reading: included only conservatively.</summary>
    Presumed = 4,

    /// <summary>Not assessed (GOV: open finding without an SLA date; REG: scenario without an assessed treatment).</summary>
    NotAssessed = 5
}

/// <summary>
/// The population an object belongs to in its category (S39 §2.3, §4.3). GOV and CTL split their weight between
/// populations by the profile's quotas (<c>cotas_populacao</c>); REG, EXP and AME have one population each.
/// </summary>
public enum RiskIndexPopulation
{
    /// <summary>Scenarios — REG; the scenario population of GOV and of CTL (CTL.P).</summary>
    Scenarios = 1,

    /// <summary>Assets — EXP; the asset population of GOV (SLA on hosts and applications).</summary>
    Assets = 2,

    /// <summary>Assessment targets — CTL.A.</summary>
    Assessments = 3,

    /// <summary>Vision One tenants — CTL.C.</summary>
    Tenants = 4,

    /// <summary>Entities targeted by incidents — AME.</summary>
    Targets = 5,

    /// <summary>Suppliers — TER.</summary>
    Suppliers = 6
}

/// <summary>The normalized asset class of an object (S39 §5.4). Stored as <c>risk_index_object_days.asset_class</c>.</summary>
public enum RiskIndexAssetClass
{
    Server = 1,
    Workstation = 2,
    Network = 3,
    Identity = 4,
    Application = 5,
    InternetFacing = 6,
    Cloud = 7,
    ExternalDomain = 8,
    OtIot = 9,
    Unknown = 10
}

/// <summary>
/// What the dashboard may show for a node on a day, by coverage (S39 §8.6; S40 §8.3). Stored on the headline row of
/// <c>risk_index_snapshots</c> (<c>display_state</c>); a category row leaves it null. Same values as the engine's.
/// </summary>
public enum RiskIndexDisplayState
{
    /// <summary>COV at or above <c>cov_ponto</c> and no weighted category unavailable: value and band.</summary>
    Point = 1,

    /// <summary>Lower coverage, or a weighted category unavailable: value, band and interval.</summary>
    PointWithInterval = 2,

    /// <summary>COV below <c>cov_indicativo</c>: "indicative" — value and interval, no band.</summary>
    Indicative = 3,

    /// <summary><c>K(S)</c> is empty: the row's values are zero and mean nothing.</summary>
    NoData = 4
}

/// <summary>
/// How a category stood in a node on a day (S39 §8.1; S40 §8.3). Stored on a category row of
/// <c>risk_index_snapshots</c> (<c>category_state</c>); the headline row leaves it null. Only an
/// <see cref="Available"/> category's value is a <c>C_k</c>; the others store 0.
/// </summary>
public enum RiskIndexCategoryState
{
    /// <summary>In <c>K(S)</c>.</summary>
    Available = 1,

    /// <summary>Weighted, with inventory, but no enabled source, no intake or nothing assessed: renormalized away.</summary>
    Unavailable = 2,

    /// <summary>No inventory in the node (REG and AME in an asset-class node).</summary>
    NotApplicable = 3,

    /// <summary>Weight 0 in the profile (TER until M48). Not in the S40 §8.3 DTO, which shows it as not applicable.</summary>
    Disabled = 4
}

/// <summary>
/// A discontinuity of one node's series on one day (S39 §9.5; S40 §9.3): the trend's arrow never crosses it and the EWMA
/// restarts there. Detected by the snapshot job and stored on the headline row (<c>risk_index_snapshots.markers</c>).
/// </summary>
public enum RiskIndexMarkerKind
{
    /// <summary>The profile in force differs from the previous snapshot's (a new version, a preset, a source enabled).</summary>
    Profile = 1,

    /// <summary>The mode differs from the previous snapshot's (always with a new profile version).</summary>
    Mode = 2,

    /// <summary>A <c>RiskAppetite</c> changed (audited) between the two snapshot instants.</summary>
    Appetite = 3,

    /// <summary>The node's closure differs from the previous snapshot's.</summary>
    EntityMap = 4,

    /// <summary>The hash of the inputs the profile does not copy (SLA, quantitative bands, residual strategy) changed.</summary>
    ExternalInput = 5
}

/// <summary>An asset-class tab of the dashboard (S40 §5.5, §7.2): <c>risk_index_class_days.tab_group</c>.</summary>
public enum RiskIndexTabGroup
{
    /// <summary>Server, workstation, network, OT/IoT and unknown: EXP, GOV (assets) and CTL (host assessments).</summary>
    Devices = 1,

    /// <summary>Internet-facing and external domains: EXP.</summary>
    InternetFacing = 2,

    /// <summary>Identities: EXP.</summary>
    Identities = 3,

    /// <summary>Applications: EXP and GOV (assets).</summary>
    Applications = 4,

    /// <summary>Cloud assets: EXP.</summary>
    Clouds = 5
}

/// <summary>
/// Why an entity is a member of a node on a day (S40 §7.2, §9.3, §11.3): <c>risk_index_node_members.member_kind</c>. An
/// official read needs the reader's claims to cover both kinds.
/// </summary>
public enum RiskIndexNodeMemberKind
{
    /// <summary>An entity of the node's closure (tree descendants and EAV links, S39 §10.1).</summary>
    Closure = 1,

    /// <summary>The scope entity of an object attributed to the node that day; a null member is an object with none.</summary>
    Scope = 2
}

/// <summary>
/// The <c>settings</c> rows of the index (S40 §7.6, §11.4), seeded with the tables and audited by key
/// (<c>GovernanceAuditInterceptor.AuditedSettingNames</c>): who may approve a profile, and who may see the index.
/// </summary>
public static class RiskIndexSettingKeys
{
    /// <summary>The roles whose grant of <c>risk_index_approve</c> approves a profile: comma-separated role names.</summary>
    public const string ApproverRoles = "risk_index_approver_roles";

    /// <summary>What <see cref="ApproverRoles"/> means when the row is missing (<c>Model.RiskIndex.RiskIndexRoles</c> agrees).</summary>
    public const string DefaultApproverRoles = "RiskManager,RiskAdministrator";

    /// <summary><c>shadow</c> (the committee and administrators only, S39 §13.3) or <c>published</c>.</summary>
    public const string Publication = "risk_index_publication";

    public const string PublicationShadow = "shadow";

    public const string PublicationPublished = "published";
}

/// <summary>The lifecycle of a preview request (S40 §7.2, §9.6): <c>risk_index_preview_requests.status</c>.</summary>
public enum RiskIndexPreviewStatus
{
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4
}
