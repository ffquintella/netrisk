using System;
using System.Collections.Generic;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A legal or contractual requirement in the catalogue (Stage 9.11, S52 §4.1, T209): a law, a regulation, a contract or an
/// internal norm, cited by the purposes and retention of catalogued data and linked to risks — the register's
/// "requirements" group, which until now had only a framework-control number to stand on.
///
/// The organization's, written with global scope (S52 D9). A contract may name its counterparty in the third-party
/// register, which makes that third party in use (<c>ThirdPartyReferences</c>). Never deleted while anything cites it.
/// </summary>
public class LegalRequirement
{
    public int Id { get; set; }

    /// <summary>A short citation, unique in the organization case-insensitively: "LGPD art. 46".</summary>
    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public LegalRequirementKind Kind { get; set; }

    public string? Description { get; set; }

    /// <summary>Where the text is — a citation or a document id. Never fetched.</summary>
    public string? Reference { get; set; }

    /// <summary>The counterparty of a contract, when it is a registered third party.</summary>
    public int? ThirdPartyId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual ThirdParty? ThirdParty { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual User? UpdatedBy { get; set; }
}

/// <summary>
/// The LGPD catalogue of one data record (S52 §4.2, T207) — keyed by the <c>organizationData</c> node of the entity map,
/// as S51 D4 foresaw, so the third parties linked to that node are this record's processors with no new column.
///
/// <b>It describes a kind of data, never data</b>: what is held (personal, sensitive, about minors), why and under which
/// legal basis (its purposes), for how long (retention), where (locations, transfer), and which RIPD covers it. No value
/// of a data subject is stored here, and nothing in the product holds the data it describes — so an expired retention
/// signals and never deletes (S52 D5). There is no route that deletes an entry; it goes with its node.
/// </summary>
public class DataCatalogueEntry
{
    public int Id { get; set; }

    /// <summary>The <c>organizationData</c> node; one entry per node.</summary>
    public int EntityId { get; set; }

    /// <summary>NULL is "not declared" — a finding, never read as "not personal".</summary>
    public PersonalDataCategory? PersonalData { get; set; }

    /// <summary>Data of children or adolescents (LGPD art. 14). NULL is not declared.</summary>
    public bool? InvolvesMinors { get; set; }

    public bool LargeVolume { get; set; }

    public bool StrategicResearch { get; set; }

    /// <summary>The categories of data subject — kinds, never people.</summary>
    public string? DataSubjects { get; set; }

    /// <summary>The categories of data held — kinds, never values.</summary>
    public string? DataCategories { get; set; }

    public int? RetentionPeriodMonths { get; set; }

    /// <summary>The event the retention period runs from.</summary>
    public string? RetentionTrigger { get; set; }

    public string? RetentionBasis { get; set; }

    public int? RetentionRequirementId { get; set; }

    /// <summary>UTC. When the retention must be reviewed or the set eliminated — the one date that signals (S52 D5).</summary>
    public DateTime? RetentionReviewDueAt { get; set; }

    /// <summary>UTC. The last review of the retention.</summary>
    public DateTime? RetentionReviewedAt { get; set; }

    public bool? InternationalTransfer { get; set; }

    public InternationalTransferMechanism? TransferMechanism { get; set; }

    public string? Notes { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual Entity Entity { get; set; } = null!;

    public virtual LegalRequirement? RetentionRequirement { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual User? UpdatedBy { get; set; }

    public virtual ICollection<DataCataloguePurpose> Purposes { get; set; } = new List<DataCataloguePurpose>();

    public virtual ICollection<DataCatalogueLocation> Locations { get; set; } = new List<DataCatalogueLocation>();
}

/// <summary>
/// One purpose of a catalogued data record with its legal basis (S52 §4.2, D2): the LGPD ties a basis to a purpose, so a
/// record processed for enrolment (contract) and for research (consent) says both.
/// </summary>
public class DataCataloguePurpose
{
    public int Id { get; set; }

    public int EntryId { get; set; }

    public string Purpose { get; set; } = null!;

    /// <summary>NULL is "not declared" — a finding for personal data (T210).</summary>
    public LgpdLegalBasis? LegalBasis { get; set; }

    /// <summary>The obligation or contract the basis rests on.</summary>
    public int? LegalRequirementId { get; set; }

    /// <summary>A reference to the consent model, the legitimate-interest assessment or the contract.</summary>
    public string? BasisReference { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual DataCatalogueEntry Entry { get; set; } = null!;

    public virtual LegalRequirement? LegalRequirement { get; set; }

    public virtual User? CreatedBy { get; set; }
}

/// <summary>Where a catalogued data record is, by country and use (S52 §4.2).</summary>
public class DataCatalogueLocation
{
    public int Id { get; set; }

    public int EntryId { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string Country { get; set; } = null!;

    public string? Region { get; set; }

    public DataLocationPurpose Purpose { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual DataCatalogueEntry Entry { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}

/// <summary>
/// A data protection impact assessment — the LGPD's <em>relatório de impacto à proteção de dados pessoais</em> (RIPD) — as
/// an artifact (S52 §4.4, T208), linked to the data records and the processes it covers. Approved by a person who is never
/// the third line, then frozen; retired with a reason; never deleted (S52 D12).
/// </summary>
public class Dpia
{
    public int Id { get; set; }

    public string Title { get; set; } = null!;

    public DpiaStatus Status { get; set; }

    /// <summary>The processing, its necessity and proportionality, the risks to data subjects and the measures — a summary.</summary>
    public string? Summary { get; set; }

    /// <summary>Where the RIPD document is. Never fetched.</summary>
    public string? DocumentReference { get; set; }

    public DpiaResidualRisk? ResidualRisk { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? PerformedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? NextReviewDueAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? ApprovedAt { get; set; }

    public int? ApprovedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? RetiredAt { get; set; }

    public int? RetiredById { get; set; }

    public string? RetireReason { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual User? ApprovedBy { get; set; }

    public virtual User? RetiredBy { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual User? UpdatedBy { get; set; }

    public virtual ICollection<DpiaLink> Links { get; set; } = new List<DpiaLink>();
}

/// <summary>What a RIPD covers: a data record or a business process (S52 §4.4); the kind is derived from the entity.</summary>
public class DpiaLink
{
    public int Id { get; set; }

    public int DpiaId { get; set; }

    public int EntityId { get; set; }

    public DpiaLinkKind Kind { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Dpia Dpia { get; set; } = null!;

    public virtual Entity Entity { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}

/// <summary>
/// A catalogued requirement linked to a risk (S52 §4.5, T209) — the register's "legal, regulatory and contractual
/// requirements", as links instead of free text. Visible exactly when the risk is.
/// </summary>
public class RiskLegalRequirement
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public int LegalRequirementId { get; set; }

    /// <summary>How the risk relates to the obligation.</summary>
    public string? Note { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Risk Risk { get; set; } = null!;

    public virtual LegalRequirement LegalRequirement { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}
