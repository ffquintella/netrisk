using DAL.Enums;

namespace Model.ThirdParties;

/// <summary>
/// The bounds every input of Stage 9.10 is validated against (S51 §4) — one place, so the services, the pure rules, the
/// parser and the tests agree.
/// </summary>
public static class ThirdPartyLimits
{
    public const int MaxNameLength = 200;
    public const int MaxLegalNameLength = 300;
    public const int MaxTaxIdLength = 50;
    public const int MaxDescriptionLength = 2000;
    public const int MaxWebsiteLength = 500;
    public const int MaxContractReferenceLength = 200;
    public const int MaxAuditClauseReferenceLength = 500;
    public const int MaxExitPlanLength = 8000;
    public const int MaxDataPortabilityLength = 2000;
    public const int MaxLinkDescriptionLength = 500;
    public const int MaxSubprocessorNameLength = 200;
    public const int MaxSubprocessorServiceLength = 500;
    public const int MaxRegionLength = 200;

    /// <summary>The sub-processors one third party may declare.</summary>
    public const int MaxSubprocessors = 200;

    /// <summary>The data locations one third party may declare.</summary>
    public const int MaxDataLocations = 100;

    /// <summary>365 days: the longest contracted RTO or RPO accepted, as for a BIA objective (S43 §4).</summary>
    public const int MaxDurationMinutes = 525_600;

    public const int MaxVulnerabilityFixDays = 3650;

    /// <summary>
    /// FGV's NRM §5.2: the supplier's deadline to fix a vulnerability of medium severity or higher "must not exceed one
    /// month". A longer contracted deadline is a finding (S51 §4.7).
    /// </summary>
    public const int NrmVulnerabilityFixDays = 30;

    public const int MaxFrameworkVersionLength = 20;

    /// <summary>The questions one HECVAT may be declared to have (the Full workbook has a few hundred).</summary>
    public const int MaxExpectedQuestions = 2000;

    public const int MaxEvidenceReferenceLength = 500;
    public const int MaxAssessmentNotesLength = 2000;
    public const int MaxQuestionIdLength = 20;
    public const int MaxAnswerNotesLength = 2000;
    public const int MinAnswerWeight = 1;
    public const int MaxAnswerWeight = 100;
    public const int MinVoidReasonLength = 10;
    public const int MaxVoidReasonLength = 1000;

    /// <summary>
    /// The share of the scored weight a complete HECVAT must reach to conform (S51 D13): 80 %. A constant, not a setting —
    /// a parameter that turns a non-conforming vendor into a conforming one needs its own audited write path first.
    /// </summary>
    public const decimal HecvatPassThreshold = 0.80m;

    public const int MaxSbomComponentNameLength = 200;
    public const int MaxSbomComponentVersionLength = 100;
    public const int MaxSbomFileNameLength = 255;

    /// <summary>5 MiB of UTF-8: the largest SBOM document accepted (S51 §4.6).</summary>
    public const int MaxSbomDocumentBytes = 5 * 1024 * 1024;

    /// <summary>The components one SBOM may carry; a larger document is refused, never silently truncated.</summary>
    public const int MaxSbomComponents = 10_000;

    /// <summary>The deepest JSON nesting the SBOM parser follows.</summary>
    public const int MaxSbomJsonDepth = 64;

    public const int MaxStoredComponentNameLength = 300;
    public const int MaxStoredComponentVersionLength = 200;
    public const int MaxStoredPurlLength = 1000;
    public const int MaxStoredLicenseLength = 500;
    public const int MaxSerialNumberLength = 300;
    public const int MaxSpecVersionLength = 20;

    /// <summary>The critical processes a concentration entry lists by name; the count always covers all of them.</summary>
    public const int MaxListedProcesses = 200;

    /// <summary>The unanswered question ids an assessment result lists; the count always covers all of them.</summary>
    public const int MaxListedQuestionIds = 100;
}

// --- requests ------------------------------------------------------------------------------------------------------

/// <summary>Creates or replaces a third party's record (S51 §6). Every field is replaced on <c>PUT</c>.</summary>
public class ThirdPartyRequest
{
    public string? Name { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? Country { get; set; }

    public string? Description { get; set; }

    /// <summary>An absolute http(s) URL (<c>ExternalUrlPolicy</c>).</summary>
    public string? Website { get; set; }

    /// <summary>Null: the organization's supplier (needs global scope).</summary>
    public int? EntityId { get; set; }

    public int? OwnerId { get; set; }

    /// <summary>Defaults to <see cref="ThirdPartyStatus.Prospective"/> on creation.</summary>
    public ThirdPartyStatus? Status { get; set; }

    public bool IsCloudProvider { get; set; }
    public bool IsIdentityProvider { get; set; }
    public bool? ProcessesPersonalData { get; set; }

    public string? ContractReference { get; set; }
    public DateTime? ContractStart { get; set; }
    public DateTime? ContractEnd { get; set; }
    public decimal? SlaAvailabilityPercent { get; set; }
    public int? ContractedRtoMinutes { get; set; }
    public int? ContractedRpoMinutes { get; set; }
    public int? VulnerabilityFixDays { get; set; }

    public bool? RightToAudit { get; set; }
    public string? AuditClauseReference { get; set; }

    public string? ExitPlan { get; set; }
    public DateTime? ExitPlanReviewedAt { get; set; }
    public DateTime? ExitPlanTestedAt { get; set; }
    public string? DataPortability { get; set; }
}

/// <summary>Links a third party to an IT service, a process or a data record (S51 §6, T204).</summary>
public class ThirdPartyLinkRequest
{
    public string? Description { get; set; }
}

public class ThirdPartySubprocessorRequest
{
    public string? Name { get; set; }

    /// <summary>The sub-processor's own record, when it is in the register.</summary>
    public int? SubprocessorThirdPartyId { get; set; }

    public string? Service { get; set; }
    public string? Country { get; set; }
    public bool ProcessesPersonalData { get; set; }
}

/// <summary>Replaces the whole sub-processor list and records that it was declared — an empty list included.</summary>
public class ThirdPartySubprocessorsRequest
{
    public List<ThirdPartySubprocessorRequest>? Subprocessors { get; set; }
}

public class ThirdPartyDataLocationRequest
{
    public string? Country { get; set; }
    public string? Region { get; set; }
    public ThirdPartyDataLocationPurpose? Purpose { get; set; }
}

/// <summary>Replaces the whole list of data locations.</summary>
public class ThirdPartyDataLocationsRequest
{
    public List<ThirdPartyDataLocationRequest>? Locations { get; set; }
}

/// <summary>Records a HECVAT questionnaire (S51 §4.5); the answers come separately, and may come in part.</summary>
public class ThirdPartyAssessmentRequest
{
    public HecvatVariant? Variant { get; set; }
    public string? FrameworkVersion { get; set; }

    /// <summary>How many questions the vendor was asked — what makes a partial answer measurably partial.</summary>
    public int? ExpectedQuestionCount { get; set; }

    public DateTime? RespondedAt { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? EvidenceReference { get; set; }
    public string? Notes { get; set; }
}

public class HecvatAnswerRequest
{
    public string? QuestionId { get; set; }
    public HecvatAnswer? Answer { get; set; }

    /// <summary>Yes or No; null for an informational question.</summary>
    public HecvatAnswer? PreferredAnswer { get; set; }

    /// <summary>1–100; 1 when omitted.</summary>
    public int? Weight { get; set; }

    public bool Critical { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Replaces an assessment's answers. A partial set is accepted — and scores <c>Incomplete</c>.</summary>
public class ThirdPartyAssessmentAnswersRequest
{
    public List<HecvatAnswerRequest>? Answers { get; set; }
}

public class ThirdPartyAssessmentVoidRequest
{
    public string? Reason { get; set; }
}

/// <summary>
/// Imports an SBOM (S51 §4.6). <see cref="Document"/> is the CycloneDX or SPDX JSON text itself, never a URL: the
/// server fetches nothing (S51 D9). <see cref="FileName"/> is metadata only.
/// </summary>
public class ThirdPartySbomRequest
{
    public string? ComponentName { get; set; }
    public string? ComponentVersion { get; set; }
    public string? FileName { get; set; }
    public string? Document { get; set; }
}

// --- reads ---------------------------------------------------------------------------------------------------------

/// <summary>A third party in a list (S51 §6).</summary>
public class ThirdPartySummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ThirdPartyStatus Status { get; set; }
    public int? EntityId { get; set; }
    public int? OwnerId { get; set; }
    public bool IsCloudProvider { get; set; }
    public bool IsIdentityProvider { get; set; }
    public DateTime? ContractEnd { get; set; }

    /// <summary>The state of its latest live HECVAT, or <see cref="Model.ThirdParties.HecvatState.NotAssessed"/>.</summary>
    public HecvatState HecvatState { get; set; }

    /// <summary>Distinct critical processes that depend on it (S51 §4.8); 0 when it is not active.</summary>
    public int DependentCriticalProcessCount { get; set; }

    public int FindingCount { get; set; }
}

public class ThirdPartyLinkDto
{
    public int EntityId { get; set; }
    public ThirdPartyLinkKind Kind { get; set; }
    public string? EntityName { get; set; }
    public string DefinitionName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedById { get; set; }
}

public class ThirdPartySubprocessorDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The registered sub-processor, when the reader may see it.</summary>
    public int? SubprocessorThirdPartyId { get; set; }

    public string? Service { get; set; }
    public string? Country { get; set; }
    public bool ProcessesPersonalData { get; set; }
}

public class ThirdPartyDataLocationDto
{
    public int Id { get; set; }
    public string Country { get; set; } = string.Empty;
    public string? Region { get; set; }
    public ThirdPartyDataLocationPurpose Purpose { get; set; }
}

/// <summary>The scored reading of a HECVAT (S51 §4.5), computed on read.</summary>
public class HecvatResultDto
{
    public HecvatState State { get; set; }
    public int ExpectedCount { get; set; }

    /// <summary>Distinct questions answered Yes, No or N/A.</summary>
    public int AnsweredCount { get; set; }

    public int NotApplicableCount { get; set; }

    /// <summary>Expected minus answered: what is still missing.</summary>
    public int UnansweredCount { get; set; }

    /// <summary>The share of the scored weight answered as preferred, 0–1. Null unless complete and scorable.</summary>
    public decimal? Score { get; set; }

    public decimal PassThreshold { get; set; }

    /// <summary>Critical questions answered against the preference.</summary>
    public List<string> CriticalFailures { get; set; } = new();

    /// <summary>The questions recorded as blank, up to <see cref="ThirdPartyLimits.MaxListedQuestionIds"/>.</summary>
    public List<string> BlankQuestionIds { get; set; } = new();

    public string Explanation { get; set; } = string.Empty;
}

public class HecvatAnswerDto
{
    public string QuestionId { get; set; } = string.Empty;
    public HecvatAnswer Answer { get; set; }
    public HecvatAnswer? PreferredAnswer { get; set; }
    public int Weight { get; set; }
    public bool Critical { get; set; }
    public string? Notes { get; set; }
}

public class ThirdPartyAssessmentDto
{
    public int Id { get; set; }
    public int ThirdPartyId { get; set; }
    public HecvatVariant Variant { get; set; }
    public string FrameworkVersion { get; set; } = string.Empty;
    public int ExpectedQuestionCount { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? EvidenceReference { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedById { get; set; }
    public DateTime? AnswersUpdatedAt { get; set; }
    public int? AnswersUpdatedById { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public HecvatResultDto Result { get; set; } = new();

    /// <summary>Filled only when one assessment is read.</summary>
    public List<HecvatAnswerDto> Answers { get; set; } = new();
}

public class SbomComponentDto
{
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Purl { get; set; }
    public string? License { get; set; }
}

public class ThirdPartySbomDto
{
    public int Id { get; set; }
    public int ThirdPartyId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string? ComponentVersion { get; set; }
    public SbomFormat Format { get; set; }
    public string? SpecVersion { get; set; }
    public string? SerialNumber { get; set; }
    public string DocumentSha256 { get; set; } = string.Empty;
    public int DocumentSizeBytes { get; set; }
    public string? FileName { get; set; }
    public int ComponentCount { get; set; }
    public DateTime UploadedAt { get; set; }
    public int? UploadedById { get; set; }

    /// <summary>Filled only when one SBOM is read.</summary>
    public List<SbomComponentDto> Components { get; set; } = new();
}

public class ThirdPartyFindingDto
{
    public ThirdPartyFindingCode Code { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// The tightest recovery objective what a third party supplies requires of it (S51 §4.7): the RTO (or MTPD) and RPO of
/// the services and processes it supplies and of everything that depends on them in the BIA, transitively.
/// </summary>
public class ThirdPartyContinuityRequirementDto
{
    public int? RequiredRtoMinutes { get; set; }
    public int? RtoBindingEntityId { get; set; }
    public string? RtoBindingName { get; set; }
    public int? RequiredRpoMinutes { get; set; }
    public int? RpoBindingEntityId { get; set; }
    public string? RpoBindingName { get; set; }
}

public class CriticalProcessRefDto
{
    public int EntityId { get; set; }
    public string? Name { get; set; }
}

/// <summary>One third party in a concentration dimension (S51 §4.8).</summary>
public class ConcentrationEntryDto
{
    public int ThirdPartyId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Distinct active critical processes that depend on it — each counted once however many paths reach it.</summary>
    public int DependentCriticalProcessCount { get; set; }

    /// <summary>That count over every active critical process of the organization. Null when there is none.</summary>
    public decimal? Share { get; set; }

    /// <summary>Up to <see cref="ThirdPartyLimits.MaxListedProcesses"/>, by id.</summary>
    public List<CriticalProcessRefDto> CriticalProcesses { get; set; } = new();

    /// <summary>Some of its dependents arrive through a supplier that names it as a sub-processor (a fourth party).</summary>
    public bool ReachedThroughSubprocessing { get; set; }

    /// <summary>
    /// Services and processes it supplies with no dependent declared in the BIA: the count may be low because the
    /// dependencies are not mapped, not because there are none.
    /// </summary>
    public int LinksWithoutDeclaredDependents { get; set; }
}

public class ConcentrationDimensionDto
{
    public ConcentrationDimension Dimension { get; set; }

    /// <summary>Active or exiting third parties counted in this dimension.</summary>
    public int ProviderCount { get; set; }

    /// <summary>The largest share any one of them has. Null when there is no critical process or no provider.</summary>
    public decimal? MaxShare { get; set; }

    public int? MostConcentratedThirdPartyId { get; set; }

    /// <summary>Largest count first, then name.</summary>
    public List<ConcentrationEntryDto> Entries { get; set; } = new();
}

/// <summary>Concentration by supplier, cloud and identity (S51 §4.8, T203), computed on read and never stored.</summary>
public class ThirdPartyConcentrationReportDto
{
    public DateTime ComputedAt { get; set; }

    /// <summary>Active critical processes of the organization — the denominator of every share.</summary>
    public int CriticalProcessCount { get; set; }

    public ConcentrationDimensionDto Suppliers { get; set; } = new() { Dimension = ConcentrationDimension.Supplier };
    public ConcentrationDimensionDto Cloud { get; set; } = new() { Dimension = ConcentrationDimension.Cloud };
    public ConcentrationDimensionDto Identity { get; set; } = new() { Dimension = ConcentrationDimension.Identity };

    /// <summary>
    /// The reader sees only some third parties. The counts of the ones listed are still computed over the whole
    /// organization, so they never depend on who asks (S51 D8).
    /// </summary>
    public bool IsScopeRestricted { get; set; }
}

/// <summary>A third party's full record (S51 §6).</summary>
public class ThirdPartyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Country { get; set; }
    public string? Description { get; set; }
    public string? Website { get; set; }
    public int? EntityId { get; set; }
    public int? OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public ThirdPartyStatus Status { get; set; }
    public bool IsCloudProvider { get; set; }
    public bool IsIdentityProvider { get; set; }
    public bool? ProcessesPersonalData { get; set; }
    public DateTime? SubprocessorsDeclaredAt { get; set; }
    public string? ContractReference { get; set; }
    public DateTime? ContractStart { get; set; }
    public DateTime? ContractEnd { get; set; }
    public decimal? SlaAvailabilityPercent { get; set; }
    public int? ContractedRtoMinutes { get; set; }
    public int? ContractedRpoMinutes { get; set; }
    public int? VulnerabilityFixDays { get; set; }
    public bool? RightToAudit { get; set; }
    public string? AuditClauseReference { get; set; }
    public string? ExitPlan { get; set; }
    public DateTime? ExitPlanReviewedAt { get; set; }
    public DateTime? ExitPlanTestedAt { get; set; }
    public string? DataPortability { get; set; }
    public DateTime? TerminatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedById { get; set; }

    public List<ThirdPartyLinkDto> Links { get; set; } = new();
    public List<ThirdPartySubprocessorDto> Subprocessors { get; set; } = new();
    public List<ThirdPartyDataLocationDto> DataLocations { get; set; } = new();

    /// <summary>Newest first, voided included; answers not filled.</summary>
    public List<ThirdPartyAssessmentDto> Assessments { get; set; } = new();

    /// <summary>The latest live assessment's result, or <see cref="HecvatState.NotAssessed"/>.</summary>
    public HecvatResultDto Hecvat { get; set; } = new() { State = HecvatState.NotAssessed };

    /// <summary>Newest first; components not filled.</summary>
    public List<ThirdPartySbomDto> Sboms { get; set; } = new();

    public ThirdPartyContinuityRequirementDto ContinuityRequirement { get; set; } = new();

    /// <summary>Its entry in the supplier concentration; zero when it is not active or exiting.</summary>
    public ConcentrationEntryDto Concentration { get; set; } = new();

    public List<ThirdPartyFindingDto> Findings { get; set; } = new();
}

/// <summary>A third party linked to an entity, read from the entity's side (S51 §6, T204).</summary>
public class EntityThirdPartyDto
{
    public int ThirdPartyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ThirdPartyStatus Status { get; set; }
    public ThirdPartyLinkKind Kind { get; set; }
    public string? Description { get; set; }
}
