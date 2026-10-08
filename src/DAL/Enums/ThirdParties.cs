namespace DAL.Enums;

/// <summary>
/// Where the relationship with a third party stands (Stage 9.10, S51 §4.1). Stored as <c>third_parties.status</c>
/// (int, CHECK 1–4).
///
/// Only <see cref="Active"/> and <see cref="Exiting"/> count in the concentration (S51 D6): a prospective supplier
/// supports nothing yet, and a terminated one supports nothing any more. A third party in use is never deleted — the
/// relationship ends with <see cref="Terminated"/> (S51 §4.9).
/// </summary>
public enum ThirdPartyStatus
{
    /// <summary>Under evaluation or contracting — the HECVAT and the contract terms are being gathered.</summary>
    Prospective = 1,

    /// <summary>Supplying the organization.</summary>
    Active = 2,

    /// <summary>Still supplying, while the exit plan is executed.</summary>
    Exiting = 3,

    /// <summary>The relationship ended; the record stays as evidence.</summary>
    Terminated = 4
}

/// <summary>
/// What a third party is linked to (Stage 9.10, S51 §4.2, T204). Stored as <c>third_party_links.kind</c> and derived
/// from the definition of the linked entity, never taken from the payload — as Stage 9.1 derives a chain level.
/// </summary>
public enum ThirdPartyLinkKind
{
    /// <summary>The third party supplies an IT service (Stage 9.1 <c>itService</c>).</summary>
    ItService = 1,

    /// <summary>The third party operates a business process (an outsourced process, <c>businessProcess</c>).</summary>
    BusinessProcess = 2,

    /// <summary>
    /// The third party processes a data record (<c>organizationData</c> or <c>organizationDataGroup</c>) — the record the
    /// Stage 9.11 catalogue extends (S51 D4).
    /// </summary>
    Data = 3
}

/// <summary>Why the data is in a place (Stage 9.10, S51 §4.4). Stored as <c>third_party_data_locations.purpose</c>.</summary>
public enum ThirdPartyDataLocationPurpose
{
    /// <summary>Where the data is stored at rest.</summary>
    Storage = 1,

    /// <summary>Where it is processed.</summary>
    Processing = 2,

    /// <summary>Where its backups are kept.</summary>
    Backup = 3,

    /// <summary>From where the supplier's support staff can reach it.</summary>
    SupportAccess = 4
}

/// <summary>
/// Which HECVAT questionnaire the vendor answered (Stage 9.10, S51 §4.5). Stored as
/// <c>third_party_assessments.variant</c>. HECVAT is EDUCAUSE's Higher Education Community Vendor Assessment Toolkit;
/// NetRisk ships none of its questions — the answers are recorded as the vendor gave them.
/// </summary>
public enum HecvatVariant
{
    Lite = 1,
    Full = 2,
    OnPremise = 3,

    /// <summary>HECVAT 3.x, a single workbook whose sections apply by product type.</summary>
    Unified = 4
}

/// <summary>
/// One answer of a HECVAT questionnaire (Stage 9.10, S51 §4.5). Stored as <c>third_party_assessment_answers.answer</c>
/// (CHECK 1–4) and, restricted to <see cref="Yes"/> and <see cref="No"/>, as its <c>preferred_answer</c>.
/// </summary>
public enum HecvatAnswer
{
    Yes = 1,
    No = 2,

    /// <summary>Does not apply to the product — out of the score's denominator.</summary>
    NotApplicable = 3,

    /// <summary>The question was asked and the vendor left it blank: the questionnaire is incomplete (S51 D7).</summary>
    Unanswered = 4
}

/// <summary>The SBOM document formats accepted (Stage 9.10, S51 §4.6, D9). Stored as <c>third_party_sboms.format</c>.</summary>
public enum SbomFormat
{
    /// <summary>CycloneDX, JSON encoding.</summary>
    CycloneDxJson = 1,

    /// <summary>SPDX 2.x, JSON encoding.</summary>
    SpdxJson = 2
}
