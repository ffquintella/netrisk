using DAL.Enums;

namespace Model.AiGovernance;

/// <summary>
/// The bounds every input of Stage 9.12 is validated against (S53 §4) — one place, so the service, the pure rules and the
/// tests agree.
/// </summary>
public static class AiGovernanceLimits
{
    public const int MaxNameLength = 200;
    public const int MaxPurposeLength = 2000;
    public const int MaxVersionLength = 100;
    public const int MaxNotesLength = 2000;

    /// <summary>Three years: the longest an evaluation may stay current.</summary>
    public const int MaxEvaluationAgeDays = 1096;

    /// <summary>The data records one model may declare.</summary>
    public const int MaxDataLinks = 100;

    /// <summary>The largest drift statistic accepted — far beyond any PSI or divergence a model would survive.</summary>
    public const decimal MaxDriftValue = 1000m;

    /// <summary>The decimals a metric value keeps (<c>decimal(18,6)</c>).</summary>
    public const int MetricValueScale = 6;

    /// <summary>The cases one reading may cover.</summary>
    public const int MaxSampleSize = 100_000_000;

    public const int MaxMethodLength = 500;
    public const int MaxEvidenceReferenceLength = 500;

    public const int MaxModelOutputLength = 1000;
    public const int MaxHumanDecisionLength = 1000;
    public const int MinReasonLength = 10;
    public const int MaxOverrideReasonLength = 2000;
    public const int MaxVoidOrRetireReasonLength = 1000;

    public const int MaxRiskLinkNoteLength = 500;

    /// <summary>The audit rows a history read returns at most.</summary>
    public const int MaxHistoryLimit = 1000;
}

// --- requests ------------------------------------------------------------------------------------------------------

/// <summary>
/// Registers a model, or replaces its inventory record (S53 §4.1). Every field is replaced. <see cref="Status"/> takes
/// proposed, pilot or production — a model is retired through its own route, with a reason.
/// </summary>
public class AiModelRequest
{
    public string? Name { get; set; }
    public string? Purpose { get; set; }
    public AiModelKind? Kind { get; set; }
    public AiModelSource? Source { get; set; }

    /// <summary>The vendor — a registered third party the caller can see; only for a vendor model.</summary>
    public int? ThirdPartyId { get; set; }

    public string? Version { get; set; }

    /// <summary>
    /// Since when the version is in use, never in the future. Null: on creation, no lower bound for readings; on a version
    /// change, now; otherwise unchanged. A reading or override from before it is refused (S53 D3).
    /// </summary>
    public DateTime? VersionSince { get; set; }

    /// <summary>Null: proposed on creation, unchanged on update.</summary>
    public AiModelStatus? Status { get; set; }

    /// <summary>Null: not declared — a finding, and the model is held to the high tier's metrics.</summary>
    public AiModelRiskTier? RiskTier { get; set; }

    /// <summary>Null: not declared — a finding.</summary>
    public AiHumanOversight? HumanOversight { get; set; }

    public int? OwnerId { get; set; }
    public int? EntityId { get; set; }

    /// <summary>Required: how many days a reading stays current, 1 to 1096.</summary>
    public int? MaxEvaluationAgeDays { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// Declares the data a model uses, whole (S53 §4.2). The list is required — an empty list declares that the model uses no
/// catalogued data — so a client that does not know it cannot clear it by omission.
/// </summary>
public class AiModelDataRequest
{
    public List<AiModelDataLinkRequest>? Data { get; set; }
}

public class AiModelDataLinkRequest
{
    /// <summary>An <c>organizationData</c> node of the entity map.</summary>
    public int? EntityId { get; set; }

    public AiModelDataUsage? Usage { get; set; }
}

/// <summary>
/// Records one metric reading (S53 §4.4). For the human override rate, <see cref="Value"/> and <see cref="MeasuredAt"/>
/// are not accepted: the server computes the rate from the overrides recorded in the period over
/// <see cref="SampleSize"/>, the outputs people reviewed in it (S53 D8).
/// </summary>
public class AiModelReadingRequest
{
    public AiModelMetric? Metric { get; set; }
    public decimal? Value { get; set; }
    public DateTime? MeasuredAt { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public int? SampleSize { get; set; }
    public string? Method { get; set; }
    public string? EvidenceReference { get; set; }
}

/// <summary>Records a person's decision contrary to the model's output (S53 §4.5). The author is the caller.</summary>
public class AiModelOverrideRequest
{
    public DateTime? OccurredAt { get; set; }
    public string? ModelOutput { get; set; }
    public string? HumanDecision { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Voids a reading or an override, or retires a model: a written reason of 10 to 1000 characters.</summary>
public class AiGovernanceReasonRequest
{
    public string? Reason { get; set; }
}

public class AiModelRiskLinkRequest
{
    public string? Note { get; set; }
}

// --- responses -----------------------------------------------------------------------------------------------------

public class AiModelFindingDto
{
    public AiModelFindingCode Code { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>A data record the model uses, with what its LGPD catalogue says — read through the node (S53 D4).</summary>
public class AiModelDataLinkDto
{
    public int Id { get; set; }
    public int EntityId { get; set; }
    public string? Name { get; set; }
    public AiModelDataUsage Usage { get; set; }

    /// <summary>Whether the record has a Stage 9.11 catalogue entry.</summary>
    public bool Catalogued { get; set; }

    /// <summary>The catalogue's personal-data category; null when not catalogued or not declared.</summary>
    public PersonalDataCategory? PersonalData { get; set; }
}

/// <summary>
/// One metric of a model for its current version (S53 §4.4). <see cref="Value"/> is null exactly when
/// <see cref="State"/> is <see cref="AiMetricState.NotEvaluated"/> — a missing evaluation has no number.
/// </summary>
public class AiModelMetricStateDto
{
    public AiModelMetric Metric { get; set; }

    /// <summary>Whether the model's risk tier and oversight require it (S53 §4.3).</summary>
    public bool Required { get; set; }

    public AiMetricState State { get; set; }
    public decimal? Value { get; set; }
    public int? ReadingId { get; set; }
    public DateTime? MeasuredAt { get; set; }
    public int? AgeDays { get; set; }

    /// <summary>When the current version has no reading: the last version that had one, if any.</summary>
    public string? LastEvaluatedVersion { get; set; }
}

public class AiModelEvaluationDto
{
    public AiModelEvaluationState State { get; set; }

    /// <summary>The version the evaluation is about — the model's current version.</summary>
    public string Version { get; set; } = "";

    /// <summary>Since when that version is in use; readings measured before it do not count.</summary>
    public DateTime? VersionSince { get; set; }

    public List<AiModelMetric> RequiredMetrics { get; set; } = [];
    public List<AiModelMetricStateDto> Metrics { get; set; } = [];
}

public class AiModelRiskDto
{
    public int RiskId { get; set; }
    public string? ReferenceId { get; set; }
    public string? Subject { get; set; }
    public string? Status { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedById { get; set; }
}

public class AiModelDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Purpose { get; set; } = "";
    public AiModelKind Kind { get; set; }
    public AiModelSource Source { get; set; }

    public int? ThirdPartyId { get; set; }

    /// <summary>The vendor's name — null when the caller cannot see that third party.</summary>
    public string? ThirdPartyName { get; set; }

    /// <summary>True when the vendor is a third party outside the caller's scope: named by id only, never by name.</summary>
    public bool ThirdPartyHidden { get; set; }

    public string Version { get; set; } = "";
    public DateTime? VersionSince { get; set; }
    public AiModelStatus Status { get; set; }
    public AiModelRiskTier? RiskTier { get; set; }
    public AiHumanOversight? HumanOversight { get; set; }
    public int? OwnerId { get; set; }
    public int? EntityId { get; set; }
    public int MaxEvaluationAgeDays { get; set; }
    public DateTime? DataDeclaredAt { get; set; }
    public string? Notes { get; set; }

    public DateTime? RetiredAt { get; set; }
    public int? RetiredById { get; set; }
    public string? RetireReason { get; set; }

    public List<AiModelDataLinkDto> Data { get; set; } = [];
    public AiModelEvaluationDto Evaluation { get; set; } = new();

    /// <summary>The risks linked to the model that the caller can see.</summary>
    public List<AiModelRiskDto> Risks { get; set; } = [];

    /// <summary>Links to risks the caller cannot see — counted, never named.</summary>
    public int HiddenRiskCount { get; set; }

    /// <summary>The live overrides recorded for the current version.</summary>
    public int OverrideCount { get; set; }

    public List<AiModelFindingDto> Findings { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public int? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedById { get; set; }
}

public class AiModelSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public AiModelKind Kind { get; set; }
    public AiModelSource Source { get; set; }
    public string Version { get; set; } = "";
    public AiModelStatus Status { get; set; }
    public AiModelRiskTier? RiskTier { get; set; }
    public int? EntityId { get; set; }
    public int? OwnerId { get; set; }
    public AiModelEvaluationState EvaluationState { get; set; }
    public int LinkedRiskCount { get; set; }
    public List<AiModelFindingCode> FindingCodes { get; set; } = [];
}

public class AiModelReadingDto
{
    public int Id { get; set; }
    public int ModelId { get; set; }
    public AiModelMetric Metric { get; set; }
    public decimal Value { get; set; }
    public string ModelVersion { get; set; } = "";
    public DateTime MeasuredAt { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public int? SampleSize { get; set; }
    public int? OverrideCount { get; set; }
    public string? Method { get; set; }
    public string? EvidenceReference { get; set; }
    public int? RecordedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }
    public string? VoidReason { get; set; }
}

public class AiModelOverrideDto
{
    public int Id { get; set; }
    public int ModelId { get; set; }
    public string ModelVersion { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public string ModelOutput { get; set; } = "";
    public string HumanDecision { get; set; } = "";
    public string Reason { get; set; } = "";

    /// <summary>The author — the person who recorded it.</summary>
    public int? RecordedById { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>The models a risk involves (S53 §4.6) — the AI component's view on the risk detail.</summary>
public class RiskAiModelsDto
{
    public int RiskId { get; set; }
    public List<RiskAiModelDto> Models { get; set; } = [];

    /// <summary>Links to models the caller cannot see — counted, never named. They still derive flag 11 (S53 §4.6).</summary>
    public int HiddenModelCount { get; set; }
}

public class RiskAiModelDto
{
    public int ModelId { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public AiModelStatus Status { get; set; }
    public AiModelRiskTier? RiskTier { get; set; }
    public AiModelEvaluationState EvaluationState { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// What the methodology panel's M10 reads (S53 §4.9): the models in use the caller sees, by evaluation state, and how many
/// have each metric evaluated. Computed on read.
/// </summary>
public class AiModelMetricsSummaryDto
{
    /// <summary>Models in pilot or production.</summary>
    public int InUse { get; set; }

    public int Evaluated { get; set; }
    public int Incomplete { get; set; }
    public int Stale { get; set; }
    public int NotEvaluated { get; set; }

    /// <summary>For each metric, the models in use with a current reading of it for their current version.</summary>
    public List<AiMetricCoverageDto> EvaluatedByMetric { get; set; } = [];

    /// <summary>Live overrides recorded over the models in use in the last 90 days.</summary>
    public int OverridesLast90Days { get; set; }

    public bool IsScopeRestricted { get; set; }
}

public class AiMetricCoverageDto
{
    public AiModelMetric Metric { get; set; }

    /// <summary>Models in use that require the metric.</summary>
    public int Required { get; set; }

    /// <summary>Models in use with a current reading of it for their current version.</summary>
    public int Evaluated { get; set; }
}
