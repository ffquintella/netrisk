namespace DAL.Enums;

/// <summary>
/// What a key risk indicator measures, as the methodology's Gate B names it (Stage 9.8, S49 §4.1): "unavailability,
/// data loss, number of data subjects or another KRI". Stored as <c>kris.category</c> (int, CHECK 1–4).
/// </summary>
public enum KriCategory
{
    /// <summary>Unavailability — hours down, availability percentage, missed RTO.</summary>
    Unavailability = 1,

    /// <summary>Data loss — records or volume lost, missed RPO.</summary>
    DataLoss = 2,

    /// <summary>Number of data subjects affected or exposed.</summary>
    DataSubjects = 3,

    /// <summary>Any other indicator the Phase 0 tolerance is set on.</summary>
    Other = 4
}

/// <summary>
/// Which way a KRI gets worse (S49 §4.1). Stored as <c>kris.direction</c> (int, CHECK 1–2). The tolerance is exceeded
/// strictly beyond it in that direction (S49 D3).
/// </summary>
public enum KriDirection
{
    /// <summary>Worse when it rises (hours down, records lost): exceeded when the value is above the tolerance.</summary>
    HigherIsWorse = 1,

    /// <summary>Worse when it falls (availability, backup success): exceeded when the value is below the tolerance.</summary>
    LowerIsWorse = 2
}

/// <summary>
/// The six mandatory reassessment triggers of MIGR-TI/IA Phase 7 (Stage 9.8, S49 §3.1). Stored as
/// <c>reassessment_events.trigger_type</c> (int, CHECK 1–6).
/// </summary>
public enum ReassessmentTriggerType
{
    /// <summary>1 — a change of architecture or technology. Declared.</summary>
    ArchitectureOrTechnologyChange = 1,

    /// <summary>2 — a new supplier, an acquisition or a migration. Declared (the third-party register is Stage 9.10).</summary>
    SupplierAcquisitionOrMigration = 2,

    /// <summary>3 — a significant incident or near miss. Declared, referencing the incident: one event per incident.</summary>
    SignificantIncidentOrNearMiss = 3,

    /// <summary>4 — a new regulation. Declared.</summary>
    NewRegulation = 4,

    /// <summary>5 — a new AI model deployed. Declared (the model inventory is Stage 9.12).</summary>
    NewAiModel = 5,

    /// <summary>6 — new data, or a KRI beyond its tolerance. Detected for a KRI; declared for new data.</summary>
    NewDataOrKriBreach = 6
}

/// <summary>Where a reassessment event came from (S49 §4.5). Stored as <c>reassessment_events.origin</c> (CHECK 1–2).</summary>
public enum ReassessmentEventOrigin
{
    /// <summary>Declared by a person, with the risks it applies to.</summary>
    Declared = 1,

    /// <summary>Detected: a KRI seen beyond its tolerance opened a breach episode. Its risks come from the KRI's links.</summary>
    KriBreach = 2
}
