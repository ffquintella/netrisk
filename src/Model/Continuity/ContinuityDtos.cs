using DAL.Enums;

namespace Model.Continuity;

/// <summary>
/// A business process or IT service as the continuity list shows it (Stage 9.3, S43 §6):
/// <c>GET /Continuity/Subjects</c>. Every subject appears, with or without a BIA.
/// </summary>
public class ContinuitySubjectDto
{
    public int EntityId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary><c>businessProcess</c> or <c>itService</c>.</summary>
    public string DefinitionName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public bool HasBia { get; set; }

    public int? MtpdMinutes { get; set; }

    public int? RtoMinutes { get; set; }

    public int? RpoMinutes { get; set; }

    /// <summary>Processes only: from the MTPD when declared, else the declared property, else null.</summary>
    public int? EffectiveCriticality { get; set; }

    public CriticalitySource? CriticalitySource { get; set; }

    public ObjectiveVerificationStatus RtoStatus { get; set; }

    public ObjectiveVerificationStatus RpoStatus { get; set; }

    public int CascadeConflictCount { get; set; }

    public bool InCycle { get; set; }

    /// <summary>The highest weight among the node's threat items; 0 when it has none.</summary>
    public decimal ThreatWeight { get; set; }
}

/// <summary>Everything the continuity panel shows for one node: <c>GET /Continuity/Subjects/{entityId}</c>.</summary>
public class ContinuityProfileDto
{
    public ContinuitySubjectDto Subject { get; set; } = new();

    public BusinessImpactAnalysisDto? Bia { get; set; }

    /// <summary>The <c>criticality</c> entity property, 1–5, or null when absent or invalid.</summary>
    public int? DeclaredCriticality { get; set; }

    /// <summary>True when a declared criticality exists and is ignored because the BIA has an MTPD.</summary>
    public bool DeclaredCriticalityIgnored { get; set; }

    public ObjectiveVerificationDto RtoVerification { get; set; } = new();

    public ObjectiveVerificationDto RpoVerification { get; set; } = new();

    /// <summary>The providers this node depends on directly.</summary>
    public List<BiaDependencyDto> DependsOn { get; set; } = new();

    /// <summary>The nodes that depend on this one directly.</summary>
    public List<BiaDependencyDto> DependedOnBy { get; set; } = new();

    public ContinuityCascadeDto Cascade { get; set; } = new();

    public ContinuityThreatDto Threat { get; set; } = new();

    /// <summary>
    /// Whether the caller may declare the BIA and dependencies: the permission <b>and</b> global scope.
    /// For enabling buttons only — the server still decides every write.
    /// </summary>
    public bool CallerCanManageBia { get; set; }

    /// <summary>Whether the caller may record and void restoration tests (permission and global scope).</summary>
    public bool CallerCanRecordTests { get; set; }
}

public class BusinessImpactAnalysisDto
{
    public int Id { get; set; }

    public int EntityId { get; set; }

    public int? MtpdMinutes { get; set; }

    public int? RtoMinutes { get; set; }

    public int? RpoMinutes { get; set; }

    /// <summary>UTC.</summary>
    public DateTime AssessedAt { get; set; }

    public string? Notes { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }
}

/// <summary>
/// <c>PUT /Continuity/Subjects/{entityId}/Bia</c>: replaces the three objectives whole — a null clears
/// that objective. At least one must be set.
/// </summary>
public class BusinessImpactAnalysisRequest
{
    public int? MtpdMinutes { get; set; }

    public int? RtoMinutes { get; set; }

    public int? RpoMinutes { get; set; }

    /// <summary>UTC; omitted keeps the stored one, or takes "now" on creation. Never in the future.</summary>
    public DateTime? AssessedAt { get; set; }

    public string? Notes { get; set; }
}

/// <summary>The result of a BIA write: <see cref="Created"/> distinguishes 201 from 200.</summary>
public class BusinessImpactAnalysisWriteResult
{
    public bool Created { get; set; }

    public BusinessImpactAnalysisDto Bia { get; set; } = new();
}

public class ObjectiveVerificationDto
{
    public ContinuityObjective Objective { get; set; }

    public ObjectiveVerificationStatus Status { get; set; }

    public VerificationReason? Reason { get; set; }

    public int? DeclaredMinutes { get; set; }

    /// <summary>The measure of the test that decided the status, when it measured one.</summary>
    public int? AchievedMinutes { get; set; }

    public int? TestId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? TestedAt { get; set; }

    /// <summary>UTC: <see cref="TestedAt"/> plus the validity in force.</summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>The validity in force when this was computed.</summary>
    public int ValidityDays { get; set; }
}

public class BiaDependencyDto
{
    public int Id { get; set; }

    public int DependentEntityId { get; set; }

    public string? DependentName { get; set; }

    public int ProviderEntityId { get; set; }

    public string? ProviderName { get; set; }

    public string? Description { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }
}

/// <summary><c>POST /Continuity/Subjects/{entityId}/Dependencies</c>: the dependent is the route's entity.</summary>
public class BiaDependencyCreateRequest
{
    public int ProviderEntityId { get; set; }

    public string? Description { get; set; }
}

/// <summary>The cascade around one node (S43 §4.6).</summary>
public class ContinuityCascadeDto
{
    /// <summary>Who is affected if this node stops, transitively, by depth then id.</summary>
    public List<CascadeNodeDto> Dependents { get; set; } = new();

    /// <summary>What this node depends on, transitively, by depth then id.</summary>
    public List<CascadeNodeDto> Providers { get; set; } = new();

    /// <summary>The other members of this node's dependency cycle; empty when it is in none.</summary>
    public List<int> CycleMembers { get; set; } = new();

    public CascadeRequirementDto? RtoRequirement { get; set; }

    public CascadeRequirementDto? RpoRequirement { get; set; }

    public List<CascadeConflictDto> Conflicts { get; set; } = new();

    /// <summary>Objectives a dependent requires of this node that this node does not declare.</summary>
    public List<ContinuityObjective> ObjectivesWithoutValueUnderRequirement { get; set; } = new();

    /// <summary>The smallest MTPD among the dependents.</summary>
    public CascadeRequirementDto? TightestDependentMtpd { get; set; }

    /// <summary>How many dependents are processes with an effective criticality of 4 or more.</summary>
    public int CriticalDependentCount { get; set; }
}

public class CascadeNodeDto
{
    public int EntityId { get; set; }

    public string? Name { get; set; }

    public string DefinitionName { get; set; } = string.Empty;

    public int Depth { get; set; }
}

public class CascadeRequirementDto
{
    public int Minutes { get; set; }

    public int BindingEntityId { get; set; }

    public string? BindingName { get; set; }
}

public class CascadeConflictDto
{
    public ContinuityObjective Objective { get; set; }

    public int DeclaredMinutes { get; set; }

    public int RequiredMinutes { get; set; }

    public int BindingEntityId { get; set; }

    public string? BindingName { get; set; }
}

/// <summary>
/// The weighted threat to a node's RTO/RPO — the flag 4 basis Stage 9.5 consumes (S43 §4.6, D16).
/// </summary>
public class ContinuityThreatDto
{
    /// <summary>By weight descending, then objective, reason and via id.</summary>
    public List<ContinuityThreatItemDto> Items { get; set; } = new();

    /// <summary>The highest item weight; 0 without items. Two unverified items do not add up to 1.</summary>
    public decimal ThreatWeight { get; set; }

    public bool IsThreatened { get; set; }

    /// <summary>The configured weight of an unverified item, beside the reasons so a reader knows what 0.5 means.</summary>
    public decimal UnverifiedWeight { get; set; }
}

public class ContinuityThreatItemDto
{
    public ContinuityThreatReason Reason { get; set; }

    public ContinuityObjective Objective { get; set; }

    public ContinuityThreatClass Class { get; set; }

    public decimal Weight { get; set; }

    /// <summary>The provider an inherited item comes through.</summary>
    public int? ViaEntityId { get; set; }

    public string? ViaName { get; set; }
}

public class RestorationTestDto
{
    public int Id { get; set; }

    public int EntityId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime TestedAt { get; set; }

    public RestorationTestOutcome Outcome { get; set; }

    public int? AchievedRtoMinutes { get; set; }

    public int? AchievedRpoMinutes { get; set; }

    public int? DeclaredRtoMinutes { get; set; }

    public int? DeclaredRpoMinutes { get; set; }

    public string? EvidenceReference { get; set; }

    public string? Notes { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? RecordedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? VoidedAt { get; set; }

    public int? VoidedById { get; set; }

    public string? VoidReason { get; set; }
}

public class RestorationTestCreateRequest
{
    /// <summary>UTC; never in the future.</summary>
    public DateTime TestedAt { get; set; }

    public RestorationTestOutcome Outcome { get; set; }

    public int? AchievedRtoMinutes { get; set; }

    public int? AchievedRpoMinutes { get; set; }

    public string? EvidenceReference { get; set; }

    public string? Notes { get; set; }
}

public class RestorationTestVoidRequest
{
    public string? Reason { get; set; }
}

/// <summary>The Phase 7 metric "restoration tested vs declared RTO/RPO" (S43 §6).</summary>
public class RestorationVerificationMetricDto
{
    /// <summary>UTC.</summary>
    public DateTime ComputedAt { get; set; }

    public int ValidityDays { get; set; }

    public decimal UnverifiedWeight { get; set; }

    public RestorationVerificationSummaryDto All { get; set; } = new();

    public RestorationVerificationSummaryDto CriticalProcesses { get; set; } = new();

    /// <summary>Active critical processes with a confirmed threat (weight 1.0).</summary>
    public int ThreatenedCriticalProcessesConfirmed { get; set; }

    /// <summary>Active critical processes whose only threats are unverified (weight = the unverified weight).</summary>
    public int ThreatenedCriticalProcessesUnverifiedOnly { get; set; }

    /// <summary>Σ ThreatWeight over the active critical processes — one unverified-only process counts w of a process.</summary>
    public decimal ThreatenedCriticalProcessesWeighted { get; set; }

    /// <summary>The active subjects.</summary>
    public List<ContinuitySubjectDto> Rows { get; set; } = new();
}

public class RestorationVerificationSummaryDto
{
    public ObjectiveVerificationSummaryDto Rto { get; set; } = new();

    public ObjectiveVerificationSummaryDto Rpo { get; set; } = new();

    /// <summary>Active subjects with no BIA — outside every denominator, never counted as 0.</summary>
    public int SubjectsWithoutBia { get; set; }
}

public class ObjectiveVerificationSummaryDto
{
    public int Declared { get; set; }

    public int Met { get; set; }

    public int NotMet { get; set; }

    public int Unverified { get; set; }

    public int UnverifiedNoTest { get; set; }

    public int UnverifiedStale { get; set; }

    public int UnverifiedNotMeasured { get; set; }

    /// <summary><see cref="Met"/> / <see cref="Declared"/>, unrounded; null when nothing is declared.</summary>
    public decimal? MetRatio { get; set; }
}

/// <summary>The two continuity parameters in force (S43 §4.7): <c>GET/PUT /Continuity/Settings</c>.</summary>
public class ContinuitySettingsDto
{
    public int RestorationTestValidityDays { get; set; }

    public decimal UnverifiedThreatWeight { get; set; }

    /// <summary>Keys whose stored value was missing or invalid and was replaced by the default.</summary>
    public List<string> FallbackApplied { get; set; } = new();
}

public class ContinuitySettingsRequest
{
    public int? RestorationTestValidityDays { get; set; }

    public decimal? UnverifiedThreatWeight { get; set; }
}
