namespace Model.DataCatalogue;

/// <summary>
/// What is missing, or does not hold, in the LGPD catalogue of a data record (Stage 9.11, S52 §4.6), computed on read and
/// never stored. A finding is a signal — nothing is refused, and nothing is ever deleted, because of one (S52 D3, D5).
/// Absent is a finding of its own, never read as compliant (S52 D4). Computed enums start at 1 (S43 D17).
/// </summary>
public enum DataCatalogueFindingCode
{
    /// <summary>The data record has no catalogue — which is not "no personal data".</summary>
    NotCatalogued = 1,

    /// <summary>Whether it holds personal data is not declared.</summary>
    PersonalDataUndeclared = 2,

    /// <summary>Personal or sensitive data with no purpose declared.</summary>
    PurposeMissing = 3,

    /// <summary>Personal or sensitive data with no legal basis declared for some purpose, or for none (T210).</summary>
    LegalBasisMissing = 4,

    /// <summary>Sensitive personal data with a purpose under a basis of art. 7º that art. 11 does not admit.</summary>
    LegalBasisNotValidForSensitiveData = 5,

    /// <summary>A purpose under a legal obligation that cites no catalogued requirement.</summary>
    LegalObligationUnnamed = 6,

    /// <summary>A purpose under legitimate interest with no reference to its balancing test.</summary>
    LegitimateInterestUnassessed = 7,

    /// <summary>Personal or sensitive data with neither a retention period nor a review date.</summary>
    RetentionUndeclared = 8,

    /// <summary>The retention review date has passed — a signal, never a deletion (T210).</summary>
    RetentionExpired = 9,

    /// <summary>Personal or sensitive data with no location, its own or its processors'.</summary>
    LocationUndeclared = 10,

    /// <summary>The data is in a country outside Brazil — its own location or a processor's — and no transfer is declared.</summary>
    InternationalTransferUndeclared = 11,

    /// <summary>An international transfer is declared without its LGPD art. 33 safeguard.</summary>
    TransferMechanismMissing = 12,

    /// <summary>High-risk processing (sensitive, minors, large volume) with no approved RIPD.</summary>
    DpiaMissing = 13,

    /// <summary>An approved RIPD covering it is past its review date.</summary>
    DpiaReviewOverdue = 14,

    /// <summary>An approved RIPD covering it concluded a high residual risk to the data subjects.</summary>
    DpiaResidualRiskHigh = 15
}
