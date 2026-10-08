using System;
using System.Collections.Generic;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A third party — a supplier, a cloud, an identity provider — as a first-class record (Stage 9.10, S51 §4.1, T201),
/// distinct from the generic <c>organization</c> node of the entity map, which carries a name and an employee count and
/// nothing a supplier needs: its contract, SLA and contracted RTO/RPO, the right to audit, the exit plan and data
/// portability, the HECVAT the vendor answered, the SBOM of what it supplies, its sub-processors and where the data is.
///
/// Entity-scoped like a KRI or a committee: <c>entity_id</c> null is the organization's supplier, seen by every reader;
/// a supplier of an entity is seen by that entity's readers. Never deleted while anything refers to it — the
/// relationship ends with <see cref="ThirdPartyStatus.Terminated"/> (S51 §4.9).
/// </summary>
public class ThirdParty : DAL.Interfaces.IEntityScoped
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? LegalName { get; set; }

    /// <summary>The company's registration number (CNPJ or the foreign equivalent).</summary>
    public string? TaxId { get; set; }

    /// <summary>ISO 3166-1 alpha-2 of the supplier's seat.</summary>
    public string? Country { get; set; }

    public string? Description { get; set; }

    /// <summary>Validated by <c>ExternalUrlPolicy</c> on the way in, so a client may open it.</summary>
    public string? Website { get; set; }

    public int? EntityId { get; set; }

    /// <summary>The person who owns the relationship — the first line (never the third, S51 §4.1).</summary>
    public int? OwnerId { get; set; }

    public ThirdPartyStatus Status { get; set; } = ThirdPartyStatus.Prospective;

    /// <summary>Counted in the cloud concentration (S51 §4.8).</summary>
    public bool IsCloudProvider { get; set; }

    /// <summary>Counted in the identity concentration (S51 §4.8).</summary>
    public bool IsIdentityProvider { get; set; }

    /// <summary>Whether it processes personal data for the organization (an LGPD operator). Null: not declared.</summary>
    public bool? ProcessesPersonalData { get; set; }

    /// <summary>UTC. When the sub-processor list was last declared — even empty. Null: never declared.</summary>
    public DateTime? SubprocessorsDeclaredAt { get; set; }

    public string? ContractReference { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? ContractStart { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? ContractEnd { get; set; }

    /// <summary>The contracted availability, in percent (99.9).</summary>
    public decimal? SlaAvailabilityPercent { get; set; }

    /// <summary>The recovery time the contract commits to, in minutes. Null: not contracted — never 0 (S51 D10).</summary>
    public int? ContractedRtoMinutes { get; set; }

    /// <summary>The data window the contract commits to, in minutes. Null: not contracted.</summary>
    public int? ContractedRpoMinutes { get; set; }

    /// <summary>The contracted maximum days to fix a vulnerability of medium severity or higher (FGV NRM §5.2).</summary>
    public int? VulnerabilityFixDays { get; set; }

    /// <summary>Whether the contract grants the organization the right to audit. Null: not declared.</summary>
    public bool? RightToAudit { get; set; }

    /// <summary>The clause that grants it.</summary>
    public string? AuditClauseReference { get; set; }

    public string? ExitPlan { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? ExitPlanReviewedAt { get; set; }

    /// <summary>UTC. When the exit plan was last exercised.</summary>
    public DateTime? ExitPlanTestedAt { get; set; }

    /// <summary>How the organization's data comes back: format, mechanism and deadline.</summary>
    public string? DataPortability { get; set; }

    /// <summary>UTC. Set exactly when the status is <see cref="ThirdPartyStatus.Terminated"/>.</summary>
    public DateTime? TerminatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual User? Owner { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual User? UpdatedBy { get; set; }

    public virtual ICollection<ThirdPartyLink> Links { get; set; } = new List<ThirdPartyLink>();

    public virtual ICollection<ThirdPartySubprocessor> Subprocessors { get; set; } = new List<ThirdPartySubprocessor>();

    public virtual ICollection<ThirdPartyDataLocation> DataLocations { get; set; } = new List<ThirdPartyDataLocation>();
}

/// <summary>
/// A third party linked to an IT service, a business process or a data record (S51 §4.2, T204). The kind is derived from
/// the linked entity. The data link targets the <c>organizationData</c> node the Stage 9.11 catalogue extends, so M49
/// reads "who processes this record" from here without a new column (S51 D4).
/// </summary>
public class ThirdPartyLink
{
    public int Id { get; set; }

    public int ThirdPartyId { get; set; }

    public int EntityId { get; set; }

    public ThirdPartyLinkKind Kind { get; set; }

    public string? Description { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual ThirdParty ThirdParty { get; set; } = null!;

    public virtual Entity Entity { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}

/// <summary>
/// A sub-processor of a third party (LGPD's <em>suboperador</em>, S51 §4.3). When the sub-processor is itself in the
/// register, <see cref="SubprocessorThirdPartyId"/> names it, and everything that depends on the third party depends on
/// it too — the fourth-party path of the concentration (S51 §4.8).
/// </summary>
public class ThirdPartySubprocessor
{
    public int Id { get; set; }

    public int ThirdPartyId { get; set; }

    public string Name { get; set; } = null!;

    public int? SubprocessorThirdPartyId { get; set; }

    /// <summary>What it does for the third party.</summary>
    public string? Service { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? Country { get; set; }

    public bool ProcessesPersonalData { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual ThirdParty ThirdParty { get; set; } = null!;

    public virtual ThirdParty? SubprocessorThirdParty { get; set; }

    public virtual User? CreatedBy { get; set; }
}

/// <summary>Where a third party keeps or reaches the organization's data (S51 §4.4).</summary>
public class ThirdPartyDataLocation
{
    public int Id { get; set; }

    public int ThirdPartyId { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string Country { get; set; } = null!;

    /// <summary>A region or data centre, as the supplier names it.</summary>
    public string? Region { get; set; }

    public ThirdPartyDataLocationPurpose Purpose { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual ThirdParty ThirdParty { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}

/// <summary>
/// A HECVAT questionnaire a vendor answered (S51 §4.5). How many questions it has is declared when it is recorded, so a
/// partial upload is measurably partial: it scores <c>Incomplete</c>, never a pass (T205, S51 D7). Voided with a reason,
/// never deleted — it is evidence.
/// </summary>
public class ThirdPartyAssessment
{
    public int Id { get; set; }

    public int ThirdPartyId { get; set; }

    public HecvatVariant Variant { get; set; }

    /// <summary>The questionnaire's version, as EDUCAUSE numbers it ("3.06").</summary>
    public string FrameworkVersion { get; set; } = null!;

    /// <summary>How many questions the vendor was asked.</summary>
    public int ExpectedQuestionCount { get; set; }

    /// <summary>UTC. When the vendor answered.</summary>
    public DateTime? RespondedAt { get; set; }

    /// <summary>UTC. After this the assessment reads <c>Expired</c>, never a pass.</summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>Where the vendor's original workbook is kept.</summary>
    public string? EvidenceReference { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// UTC. When the answers were last replaced. The answers are not in the audit allowlist (up to two thousand rows per
    /// upload, S51 D12); this column is, so every replacement is in the trail with its author.
    /// </summary>
    public DateTime? AnswersUpdatedAt { get; set; }

    public int? AnswersUpdatedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? VoidedAt { get; set; }

    public int? VoidedById { get; set; }

    public string? VoidReason { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual ThirdParty ThirdParty { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }

    public virtual User? AnswersUpdatedBy { get; set; }

    public virtual User? VoidedBy { get; set; }

    public virtual ICollection<ThirdPartyAssessmentAnswer> Answers { get; set; } = new List<ThirdPartyAssessmentAnswer>();
}

/// <summary>One answered (or left blank) HECVAT question (S51 §4.5). Unique per assessment and question id.</summary>
public class ThirdPartyAssessmentAnswer
{
    public int Id { get; set; }

    public int AssessmentId { get; set; }

    /// <summary>The question's id in the workbook ("HFIH-01"), upper-cased.</summary>
    public string QuestionId { get; set; } = null!;

    public HecvatAnswer Answer { get; set; }

    /// <summary>The answer the institution prefers — Yes or No. Null: an informational question, not scored.</summary>
    public HecvatAnswer? PreferredAnswer { get; set; }

    /// <summary>1–100.</summary>
    public int Weight { get; set; } = 1;

    /// <summary>A deal-breaker: answered against the preference, the assessment is non-conforming whatever the score.</summary>
    public bool Critical { get; set; }

    /// <summary>The vendor's additional information.</summary>
    public string? Notes { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual ThirdPartyAssessment Assessment { get; set; } = null!;
}

/// <summary>
/// The software bill of materials of a component a third party supplies (S51 §4.6). The document is parsed on the way
/// in — CycloneDX or SPDX JSON, size- and depth-limited — and its components stored; the document itself is not kept,
/// only its SHA-256, size and the file name as metadata (S51 D9).
/// </summary>
public class ThirdPartySbom
{
    public int Id { get; set; }

    public int ThirdPartyId { get; set; }

    /// <summary>The supplied product the SBOM describes.</summary>
    public string ComponentName { get; set; } = null!;

    public string? ComponentVersion { get; set; }

    public SbomFormat Format { get; set; }

    public string? SpecVersion { get; set; }

    /// <summary>CycloneDX <c>serialNumber</c> or SPDX <c>documentNamespace</c>.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Lower-case hexadecimal SHA-256 of the document as received.</summary>
    public string DocumentSha256 { get; set; } = null!;

    public int DocumentSizeBytes { get; set; }

    /// <summary>The last segment of the name the client gave, as metadata only — never used as a path.</summary>
    public string? FileName { get; set; }

    public int ComponentCount { get; set; }

    /// <summary>UTC.</summary>
    public DateTime UploadedAt { get; set; }

    public int? UploadedById { get; set; }

    public virtual ThirdParty ThirdParty { get; set; } = null!;

    public virtual User? UploadedBy { get; set; }

    public virtual ICollection<ThirdPartySbomComponent> Components { get; set; } = new List<ThirdPartySbomComponent>();
}

/// <summary>One component of an SBOM (S51 §4.6).</summary>
public class ThirdPartySbomComponent
{
    public int Id { get; set; }

    public int SbomId { get; set; }

    public string Name { get; set; } = null!;

    public string? Version { get; set; }

    /// <summary>The package URL.</summary>
    public string? Purl { get; set; }

    public string? License { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual ThirdPartySbom Sbom { get; set; } = null!;
}
