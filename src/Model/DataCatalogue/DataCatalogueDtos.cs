using DAL.Enums;

namespace Model.DataCatalogue;

/// <summary>
/// The bounds every input of Stage 9.11 is validated against (S52 §4) — one place, so the service, the pure rules and the
/// tests agree.
/// </summary>
public static class DataCatalogueLimits
{
    public const int MaxDataSubjectsLength = 500;
    public const int MaxDataCategoriesLength = 1000;
    public const int MaxNotesLength = 2000;
    public const int MaxRetentionTriggerLength = 500;
    public const int MaxRetentionBasisLength = 1000;

    /// <summary>One hundred years: the longest retention period accepted.</summary>
    public const int MaxRetentionMonths = 1200;

    /// <summary>The purposes one data record may declare.</summary>
    public const int MaxPurposes = 50;

    public const int MaxPurposeLength = 300;
    public const int MaxBasisReferenceLength = 500;

    /// <summary>The locations one data record may declare.</summary>
    public const int MaxLocations = 50;

    public const int MaxRegionLength = 200;

    public const int MaxRequirementCodeLength = 100;
    public const int MaxRequirementTitleLength = 300;
    public const int MaxRequirementDescriptionLength = 2000;
    public const int MaxRequirementReferenceLength = 500;

    public const int MaxDpiaTitleLength = 200;
    public const int MaxDpiaSummaryLength = 20_000;
    public const int MaxDpiaDocumentReferenceLength = 500;
    public const int MinRetireReasonLength = 10;
    public const int MaxRetireReasonLength = 1000;

    public const int MaxRiskLinkNoteLength = 500;

    /// <summary>The audit rows a history read returns at most.</summary>
    public const int MaxHistoryLimit = 1000;

    /// <summary>
    /// The LGPD's jurisdiction: a location outside it is an international transfer (art. 33). Fixed — an installation
    /// outside Brazil would read every location as a transfer (S52 R7).
    /// </summary>
    public const string HomeCountry = "BR";
}

// --- requests ------------------------------------------------------------------------------------------------------

/// <summary>
/// Catalogues a data record, or replaces its catalogue (S52 §4.2). Every field is replaced. The two lists are required —
/// an empty list declares "none" — so a client that does not know them cannot clear them by omission.
///
/// <b>Kinds, never values</b>: data subjects, data categories, purposes and notes describe what is held, and a text that
/// carries an e-mail address or a formatted CPF is refused (S52 D13).
/// </summary>
public class DataCatalogueEntryRequest
{
    /// <summary>Null: not declared — a finding.</summary>
    public PersonalDataCategory? PersonalData { get; set; }

    public bool? InvolvesMinors { get; set; }
    public bool LargeVolume { get; set; }
    public bool StrategicResearch { get; set; }

    public string? DataSubjects { get; set; }
    public string? DataCategories { get; set; }

    public int? RetentionPeriodMonths { get; set; }
    public string? RetentionTrigger { get; set; }
    public string? RetentionBasis { get; set; }
    public int? RetentionRequirementId { get; set; }
    public DateTime? RetentionReviewDueAt { get; set; }
    public DateTime? RetentionReviewedAt { get; set; }

    public bool? InternationalTransfer { get; set; }

    /// <summary>Only with a declared transfer.</summary>
    public InternationalTransferMechanism? TransferMechanism { get; set; }

    public string? Notes { get; set; }

    public List<DataCataloguePurposeRequest>? Purposes { get; set; }

    public List<DataCatalogueLocationRequest>? Locations { get; set; }
}

public class DataCataloguePurposeRequest
{
    public string? Purpose { get; set; }

    /// <summary>Null: not declared — a finding for personal data.</summary>
    public LgpdLegalBasis? LegalBasis { get; set; }

    public int? LegalRequirementId { get; set; }
    public string? BasisReference { get; set; }
}

public class DataCatalogueLocationRequest
{
    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? Country { get; set; }

    public string? Region { get; set; }
    public DataLocationPurpose? Purpose { get; set; }
}

/// <summary>Creates or replaces a legal or contractual requirement (S52 §4.1).</summary>
public class LegalRequirementRequest
{
    public string? Code { get; set; }
    public string? Title { get; set; }
    public LegalRequirementKind? Kind { get; set; }
    public string? Description { get; set; }
    public string? Reference { get; set; }

    /// <summary>The counterparty of a contract — only for <see cref="LegalRequirementKind.Contract"/>.</summary>
    public int? ThirdPartyId { get; set; }
}

/// <summary>Creates or replaces a draft RIPD (S52 §4.4).</summary>
public class DpiaRequest
{
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? DocumentReference { get; set; }
    public DpiaResidualRisk? ResidualRisk { get; set; }
    public DateTime? PerformedAt { get; set; }
    public DateTime? NextReviewDueAt { get; set; }
}

public class DpiaRetireRequest
{
    public string? Reason { get; set; }
}

/// <summary>Links a catalogued requirement to a risk (S52 §4.5).</summary>
public class RiskLegalRequirementRequest
{
    public string? Note { get; set; }
}

// --- responses -----------------------------------------------------------------------------------------------------

public class DataCatalogueFindingDto
{
    public DataCatalogueFindingCode Code { get; set; }
    public string Message { get; set; } = "";
}

public class LegalRequirementRefDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public LegalRequirementKind Kind { get; set; }
}

public class DataCataloguePurposeDto
{
    public int Id { get; set; }
    public string Purpose { get; set; } = "";
    public LgpdLegalBasis? LegalBasis { get; set; }

    /// <summary>"LGPD art. 7º, IX"; null when no basis is declared.</summary>
    public string? LegalBasisArticle { get; set; }

    public LegalRequirementRefDto? LegalRequirement { get; set; }
    public string? BasisReference { get; set; }
}

public class DataCatalogueLocationDto
{
    public int Id { get; set; }
    public string Country { get; set; } = "";
    public string? Region { get; set; }
    public DataLocationPurpose Purpose { get; set; }
}

/// <summary>A third party that processes the record (a type-3 link to it or to its group), visible to the reader.</summary>
public class DataRecordProcessorDto
{
    public int ThirdPartyId { get; set; }
    public string Name { get; set; } = "";
    public ThirdPartyStatus Status { get; set; }

    /// <summary>True when the link is to the record's group rather than to the record.</summary>
    public bool ThroughGroup { get; set; }
}

public class DataRecordDpiaDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DpiaStatus Status { get; set; }
    public DpiaResidualRisk? ResidualRisk { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? NextReviewDueAt { get; set; }
}

/// <summary>
/// A data record with its catalogue (S52 §4.2): the <c>organizationData</c> node, what is declared about it, who processes
/// it, where it goes and the findings. Every catalogue field is null when <see cref="Catalogued"/> is false.
/// </summary>
public class DataRecordDto
{
    public int EntityId { get; set; }
    public string? Name { get; set; }
    public bool Catalogued { get; set; }

    public PersonalDataCategory? PersonalData { get; set; }
    public bool? InvolvesMinors { get; set; }
    public bool LargeVolume { get; set; }
    public bool StrategicResearch { get; set; }
    public string? DataSubjects { get; set; }
    public string? DataCategories { get; set; }

    public int? RetentionPeriodMonths { get; set; }
    public string? RetentionTrigger { get; set; }
    public string? RetentionBasis { get; set; }
    public LegalRequirementRefDto? RetentionRequirement { get; set; }
    public DateTime? RetentionReviewDueAt { get; set; }
    public DateTime? RetentionReviewedAt { get; set; }

    public bool? InternationalTransfer { get; set; }
    public InternationalTransferMechanism? TransferMechanism { get; set; }
    public string? Notes { get; set; }

    public List<DataCataloguePurposeDto> Purposes { get; set; } = [];
    public List<DataCatalogueLocationDto> Locations { get; set; } = [];

    /// <summary>The processors the reader can see.</summary>
    public List<DataRecordProcessorDto> Processors { get; set; } = [];

    /// <summary>Processors the reader cannot see: counted, never named (S51 D8).</summary>
    public int HiddenProcessorCount { get; set; }

    /// <summary>The countries outside Brazil the data reaches — its own and its processors', computed unscoped (S52 D11).</summary>
    public List<string> TransferCountries { get; set; } = [];

    public List<DataRecordDpiaDto> Dpias { get; set; } = [];

    public List<DataCatalogueFindingDto> Findings { get; set; } = [];

    public DateTime? CreatedAt { get; set; }
    public int? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedById { get; set; }
}

/// <summary>One line of the compliance list (S52 §6, <c>GET /DataCatalogue/Records</c>).</summary>
public class DataRecordSummaryDto
{
    public int EntityId { get; set; }
    public string? Name { get; set; }
    public bool Catalogued { get; set; }
    public PersonalDataCategory? PersonalData { get; set; }
    public int PurposeCount { get; set; }
    public int ApprovedDpiaCount { get; set; }
    public List<DataCatalogueFindingCode> FindingCodes { get; set; } = [];
}

public class LegalRequirementDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public LegalRequirementKind Kind { get; set; }
    public string? Description { get; set; }
    public string? Reference { get; set; }

    /// <summary>Null when there is none, or when the reader cannot see it (<see cref="ThirdPartyHidden"/>).</summary>
    public int? ThirdPartyId { get; set; }

    public string? ThirdPartyName { get; set; }

    /// <summary>A third party is named, and the reader cannot see it.</summary>
    public bool ThirdPartyHidden { get; set; }

    /// <summary>The risks the reader can see that cite it.</summary>
    public int RiskLinkCount { get; set; }

    public int PurposeCount { get; set; }
    public int RetentionCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public int? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedById { get; set; }
}

public class DpiaLinkDto
{
    public int EntityId { get; set; }
    public DpiaLinkKind Kind { get; set; }
    public string? EntityName { get; set; }
}

public class DpiaDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DpiaStatus Status { get; set; }
    public string? Summary { get; set; }
    public string? DocumentReference { get; set; }
    public DpiaResidualRisk? ResidualRisk { get; set; }
    public DateTime? PerformedAt { get; set; }
    public DateTime? NextReviewDueAt { get; set; }

    /// <summary>Approved and past its review date.</summary>
    public bool ReviewOverdue { get; set; }

    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedById { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? RetiredAt { get; set; }
    public int? RetiredById { get; set; }
    public string? RetireReason { get; set; }

    public List<DpiaLinkDto> Links { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public int? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedById { get; set; }
}

public class DpiaSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DpiaStatus Status { get; set; }
    public DpiaResidualRisk? ResidualRisk { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? NextReviewDueAt { get; set; }
    public bool ReviewOverdue { get; set; }
    public int DataRecordCount { get; set; }
    public int ProcessCount { get; set; }
}

public class RiskRequirementDto
{
    public int RequirementId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public LegalRequirementKind Kind { get; set; }
    public string? Note { get; set; }
    public DateTime LinkedAt { get; set; }
    public int? LinkedById { get; set; }
}

/// <summary>A data record a risk reaches through its chain links, with what the catalogue says of it.</summary>
public class RiskDataRecordDto
{
    public int EntityId { get; set; }
    public string? Name { get; set; }
    public bool Catalogued { get; set; }
    public PersonalDataCategory? PersonalData { get; set; }
    public bool? InvolvesMinors { get; set; }
    public bool LargeVolume { get; set; }
    public bool StrategicResearch { get; set; }
    public List<DataCatalogueFindingCode> FindingCodes { get; set; } = [];

    /// <summary>The requirements its purposes and retention cite.</summary>
    public List<int> CitedRequirementIds { get; set; } = [];
}

/// <summary>
/// The legal and contractual side of a risk (S52 §4.5, T209): the requirements linked to it, the data records it reaches
/// through the chain with their findings, and the requirements those records cite. The evidence of whoever declares flag
/// 2, which stays declared (S52 D7).
/// </summary>
public class RiskComplianceDto
{
    public int RiskId { get; set; }
    public List<RiskRequirementDto> Requirements { get; set; } = [];
    public List<RiskDataRecordDto> DataRecords { get; set; } = [];

    /// <summary>Cited by the data records and not linked to the risk directly.</summary>
    public List<LegalRequirementRefDto> CatalogueRequirements { get; set; } = [];
}
