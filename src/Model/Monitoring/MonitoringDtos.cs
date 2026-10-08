using DAL.Enums;

namespace Model.Monitoring;

/// <summary>
/// The bounds every input of Stage 9.8 is validated against (S49 §4) — one place, so the service, the pure rules and
/// the tests agree.
/// </summary>
public static class MonitoringLimits
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxSourceLength = 500;
    public const int MaxUnitLength = 50;
    public const int MaxRationaleLength = 2000;
    public const int MaxNoteLength = 1000;
    public const int MaxVoidReasonLength = 1000;
    public const int MaxTitleLength = 200;

    /// <summary>The shortest and longest a reading may stay current (<c>ck_kris_max_reading_age_days</c>).</summary>
    public const int MinReadingAgeDays = 1;
    public const int MaxReadingAgeDays = 366;

    /// <summary>The default when a request omits it: the monthly KRI cadence of Phase 7.</summary>
    public const int DefaultMaxReadingAgeDays = 31;

    /// <summary>The largest absolute value of a reading or threshold (10¹²), well inside <c>decimal(18,4)</c>.</summary>
    public const decimal MaxValue = 1_000_000_000_000m;

    /// <summary>The risks one declared event may apply to.</summary>
    public const int MaxEventRisks = 500;

    /// <summary>The readings a KRI's detail returns, newest first.</summary>
    public const int MaxReadingsReturned = 500;

    /// <summary>The events a list returns at most, and by default.</summary>
    public const int MaxListLimit = 500;
    public const int DefaultListLimit = 100;

    /// <summary>A reading or event "in the future" by less than this is accepted as clock skew.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Nothing is observed before this.</summary>
    public static readonly DateTime Earliest = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

/// <summary>Create/update payload of a KRI (S49 §4.1). Every field is validated server-side; null means "missing".</summary>
public class KriRequest
{
    public string? Name { get; set; }

    public string? Description { get; set; }

    public KriCategory? Category { get; set; }

    public string? Source { get; set; }

    public string? Unit { get; set; }

    public KriDirection? Direction { get; set; }

    public decimal? ToleranceThreshold { get; set; }

    public decimal? WarningThreshold { get; set; }

    public string? ToleranceRationale { get; set; }

    /// <summary>Defaults to <see cref="MonitoringLimits.DefaultMaxReadingAgeDays"/>.</summary>
    public int? MaxReadingAgeDays { get; set; }

    public int? OwnerId { get; set; }

    /// <summary>Null for an organization-wide indicator.</summary>
    public int? EntityId { get; set; }
}

/// <summary>Where a KRI stands now (S49 §4.4).</summary>
public class KriStatusDto
{
    public KriState State { get; set; }

    public int? LatestReadingId { get; set; }

    public decimal? LatestValue { get; set; }

    public DateTime? LatestObservedAt { get; set; }

    /// <summary>Whole days since the latest reading was observed.</summary>
    public int? AgeDays { get; set; }

    /// <summary>The latest valid reading is beyond the tolerance — true for a stale KRI whose last reading breached.</summary>
    public bool LastReadingBreached { get; set; }

    public DateTime EvaluatedAt { get; set; }

    /// <summary>A sentence the clients can show verbatim.</summary>
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>A KRI with its current state (S49 §6).</summary>
public class KriDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public KriCategory Category { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public KriDirection Direction { get; set; }

    public decimal ToleranceThreshold { get; set; }

    public decimal? WarningThreshold { get; set; }

    public string ToleranceRationale { get; set; } = string.Empty;

    public int MaxReadingAgeDays { get; set; }

    public int? OwnerId { get; set; }

    public int? EntityId { get; set; }

    public DateTime? RetiredAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public KriStatusDto Status { get; set; } = new();

    /// <summary>The breach episode in progress — its reassessment event — when there is one.</summary>
    public int? OpenBreachEventId { get; set; }

    /// <summary>How many risks the KRI governs (those the caller can see).</summary>
    public int LinkedRisks { get; set; }
}

/// <summary>One reading of a KRI (S49 §4.2).</summary>
public class KriReadingDto
{
    public int Id { get; set; }

    public int KriId { get; set; }

    public decimal Value { get; set; }

    public DateTime ObservedAt { get; set; }

    public string? Note { get; set; }

    public int? RecordedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? VoidedAt { get; set; }

    public int? VoidedById { get; set; }

    public string? VoidReason { get; set; }

    /// <summary>Beyond the KRI's tolerance as it stands now.</summary>
    public bool BeyondTolerance { get; set; }
}

/// <summary>A risk a KRI governs.</summary>
public class KriLinkedRiskDto
{
    public int RiskId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime LinkedAt { get; set; }

    public int? LinkedById { get; set; }
}

/// <summary>A KRI with its history and the risks it governs (S49 §6).</summary>
public class KriDetailDto : KriDto
{
    /// <summary>Newest first, voided ones included; at most <see cref="MonitoringLimits.MaxReadingsReturned"/>.</summary>
    public List<KriReadingDto> Readings { get; set; } = [];

    /// <summary>More readings exist than <see cref="Readings"/> holds.</summary>
    public bool ReadingsTruncated { get; set; }

    /// <summary>The risks the caller can see.</summary>
    public List<KriLinkedRiskDto> Risks { get; set; } = [];
}

/// <summary>A reading to record (S49 §4.2).</summary>
public class KriReadingRequest
{
    public decimal? Value { get; set; }

    /// <summary>UTC. When the value was true. Never in the future.</summary>
    public DateTime? ObservedAt { get; set; }

    public string? Note { get; set; }
}

/// <summary>Why a reading is voided.</summary>
public class KriReadingVoidRequest
{
    public string? Reason { get; set; }
}

/// <summary>One KRI as Gate B by indicator read it (S49 §4.8).</summary>
public class KriGateDto
{
    public int KriId { get; set; }

    public string Name { get; set; } = string.Empty;

    public KriCategory Category { get; set; }

    public string Unit { get; set; } = string.Empty;

    public KriDirection Direction { get; set; }

    public decimal ToleranceThreshold { get; set; }

    public KriState State { get; set; }

    public decimal? Value { get; set; }

    public DateTime? ObservedAt { get; set; }

    /// <summary>This KRI makes the gate exceed: breached, or stale with its last reading breached.</summary>
    public bool Exceeds { get; set; }
}

/// <summary>
/// Gate B by indicator (Stage 9.8, S49 §4.8): the KRIs linked to a risk against their tolerances. Additive to
/// <c>AppetiteEvaluation</c> — the ordinal ceiling and the tail keep their meaning and are checked first.
/// </summary>
public class IndicatorAppetiteEvaluation
{
    public IndicatorAppetiteState State { get; set; } = IndicatorAppetiteState.NotConfigured;

    /// <summary>Why it is not assessable; empty otherwise.</summary>
    public List<IndicatorNotAssessableReason> Reasons { get; set; } = [];

    public List<KriGateDto> Kris { get; set; } = [];

    /// <summary>A sentence the clients can show verbatim.</summary>
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>A reassessment event to declare (S49 §4.7).</summary>
public class ReassessmentEventRequest
{
    public ReassessmentTriggerType? Type { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    /// <summary>UTC. When the fact happened; never in the future.</summary>
    public DateTime? OccurredAt { get; set; }

    /// <summary>The incident or near miss, on a <see cref="ReassessmentTriggerType.SignificantIncidentOrNearMiss"/> event only.</summary>
    public int? IncidentId { get; set; }

    public List<int>? RiskIds { get; set; }
}

/// <summary>Risks to apply an existing declared event to.</summary>
public class ReassessmentRisksRequest
{
    public List<int>? RiskIds { get; set; }
}

/// <summary>A reassessment trigger on one risk, with where it stands (S49 §4.6).</summary>
public class ReassessmentTriggerDto
{
    public int Id { get; set; }

    public int EventId { get; set; }

    public ReassessmentTriggerType TriggerType { get; set; }

    public ReassessmentEventOrigin Origin { get; set; }

    public string EventTitle { get; set; } = string.Empty;

    public int RiskId { get; set; }

    public string RiskSubject { get; set; } = string.Empty;

    public DateTime RaisedAt { get; set; }

    public ReassessmentTriggerState State { get; set; }

    /// <summary>The management review that answered it.</summary>
    public int? AnsweredByReviewId { get; set; }

    public DateTime? AnsweredAt { get; set; }
}

/// <summary>A reassessment event with the triggers the caller can see (S49 §4.5).</summary>
public class ReassessmentEventDto
{
    public int Id { get; set; }

    public ReassessmentTriggerType TriggerType { get; set; }

    public ReassessmentEventOrigin Origin { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime OccurredAt { get; set; }

    public int? IncidentId { get; set; }

    public int? KriId { get; set; }

    public int? KriReadingId { get; set; }

    public DateTime? KriBreachEndedAt { get; set; }

    public int? DeclaredById { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<ReassessmentTriggerDto> Triggers { get; set; } = [];

    /// <summary>On a write: closed risks named in the request, skipped.</summary>
    public List<int> SkippedClosedRiskIds { get; set; } = [];

    /// <summary>On a write: risks the event had already triggered, not triggered again.</summary>
    public List<int> AlreadyTriggeredRiskIds { get; set; } = [];

    /// <summary>
    /// On a write: archived risks named in the request whose archive watches this trigger type, reopened by it and
    /// triggered (Stage 9.9, S50 §4.3). The other closed risks stay in <see cref="SkippedClosedRiskIds"/>.
    /// </summary>
    public List<int> ReopenedArchivedRiskIds { get; set; } = [];
}

/// <summary>What one evaluation pass over every KRI did (the nightly job, S49 §4.7).</summary>
public class KriEvaluationSummary
{
    public int KrisEvaluated { get; set; }

    public int Breached { get; set; }

    public int Stale { get; set; }

    public int EpisodesOpened { get; set; }

    public int EpisodesClosed { get; set; }

    public int TriggersRaised { get; set; }

    /// <summary>Archived risks a breach reopened because their archive watches KRI breaches (Stage 9.9, S50 §4.3).</summary>
    public int ArchivesReopened { get; set; }
}

/// <summary>One of the ten Phase 7 metrics (S49 §3.3, §4.9).</summary>
public class MethodologyMetricDto
{
    /// <summary>M1–M10, in the methodology's order.</summary>
    public string Code { get; set; } = string.Empty;

    public MethodologyMetric Metric { get; set; }

    public string Name { get; set; } = string.Empty;

    public MetricAvailability Availability { get; set; }

    /// <summary>The headline value, when there is one.</summary>
    public double? Value { get; set; }

    public string? Unit { get; set; }

    /// <summary>For a ratio, its two terms.</summary>
    public double? Numerator { get; set; }

    public double? Denominator { get; set; }

    /// <summary>What the number means, what is missing, or why it is not available.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>The route that computes it in full.</summary>
    public string? Source { get; set; }

    /// <summary>The Track 9 stage that delivered it, or will.</summary>
    public string Stage { get; set; } = string.Empty;
}

/// <summary>The KRIs by state (S49 §4.9).</summary>
public class KriHealthDto
{
    public int Active { get; set; }

    public int WithinTolerance { get; set; }

    public int Warning { get; set; }

    public int Breached { get; set; }

    public int Stale { get; set; }

    public int NoReading { get; set; }

    public int Retired { get; set; }
}

/// <summary>The reassessment triggers by state (S49 §4.9).</summary>
public class ReassessmentHealthDto
{
    /// <summary>Events that occurred in the last 90 days.</summary>
    public int EventsLast90Days { get; set; }

    public int Pending { get; set; }

    public int Answered { get; set; }

    public int RiskClosed { get; set; }

    /// <summary>Mean days from a trigger to the review that answered it; null when none is answered.</summary>
    public double? MeanDaysToAnswer { get; set; }
}

/// <summary>The methodology's metrics panel (Stage 9.8, S49 §4.9). Computed on read, never stored.</summary>
public class MethodologyMetricsDto
{
    public DateTime ComputedAt { get; set; }

    /// <summary>The ten Phase 7 metrics, M1–M10.</summary>
    public List<MethodologyMetricDto> Metrics { get; set; } = [];

    public KriHealthDto Kris { get; set; } = new();

    public ReassessmentHealthDto Reassessment { get; set; } = new();
}
