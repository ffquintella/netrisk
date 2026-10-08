namespace DAL.Enums;

/// <summary>
/// What kind of data a catalogued data record holds, in the LGPD's terms (Stage 9.11, S52 §4.2). Stored as
/// <c>data_catalogue_entries.personal_data</c> (int, CHECK 1–4); NULL is "not declared" — a finding, never read as "not
/// personal" (S52 D4).
/// </summary>
public enum PersonalDataCategory
{
    /// <summary>Not related to an identified or identifiable natural person.</summary>
    NotPersonal = 1,

    /// <summary>Personal data (LGPD art. 5º, I).</summary>
    Personal = 2,

    /// <summary>Sensitive personal data (LGPD art. 5º, II): only the bases of art. 11 apply. Derives flag 5 (S52 D6).</summary>
    SensitivePersonal = 3,

    /// <summary>Anonymised (art. 12): not personal data while the anonymisation holds.</summary>
    Anonymised = 4
}

/// <summary>
/// The legal basis of a processing purpose (S52 §4.3), stored as <c>data_catalogue_purposes.legal_basis</c> (int, CHECK
/// 1–18). One value per inciso of LGPD art. 7º (1–10) and art. 11 (11–18), because sensitive personal data admits only the
/// bases of art. 11 — legitimate interest and credit protection have no counterpart there (S52 D3).
/// </summary>
public enum LgpdLegalBasis
{
    /// <summary>Art. 7º, I — consent.</summary>
    Art7Consent = 1,

    /// <summary>Art. 7º, II — compliance with a legal or regulatory obligation.</summary>
    Art7LegalObligation = 2,

    /// <summary>Art. 7º, III — the public administration, for public policies.</summary>
    Art7PublicPolicy = 3,

    /// <summary>Art. 7º, IV — studies by a research body.</summary>
    Art7Research = 4,

    /// <summary>Art. 7º, V — performing a contract, or its preliminary procedures, at the data subject's request.</summary>
    Art7Contract = 5,

    /// <summary>Art. 7º, VI — the regular exercise of rights in judicial, administrative or arbitral proceedings.</summary>
    Art7ExerciseOfRights = 6,

    /// <summary>Art. 7º, VII — protecting the life or physical safety of the data subject or a third party.</summary>
    Art7ProtectionOfLife = 7,

    /// <summary>Art. 7º, VIII — health protection, by health professionals, services or authorities.</summary>
    Art7HealthProtection = 8,

    /// <summary>Art. 7º, IX — the legitimate interests of the controller or a third party.</summary>
    Art7LegitimateInterest = 9,

    /// <summary>Art. 7º, X — credit protection.</summary>
    Art7CreditProtection = 10,

    /// <summary>Art. 11, I — specific and highlighted consent, for specific purposes.</summary>
    Art11Consent = 11,

    /// <summary>Art. 11, II, a — compliance with a legal or regulatory obligation.</summary>
    Art11LegalObligation = 12,

    /// <summary>Art. 11, II, b — the public administration, for public policies.</summary>
    Art11PublicPolicy = 13,

    /// <summary>Art. 11, II, c — studies by a research body.</summary>
    Art11Research = 14,

    /// <summary>Art. 11, II, d — the regular exercise of rights, including in a contract and in proceedings.</summary>
    Art11ExerciseOfRights = 15,

    /// <summary>Art. 11, II, e — protecting the life or physical safety of the data subject or a third party.</summary>
    Art11ProtectionOfLife = 16,

    /// <summary>Art. 11, II, f — health protection, by health professionals, services or authorities.</summary>
    Art11HealthProtection = 17,

    /// <summary>Art. 11, II, g — fraud prevention and the data subject's security in identification and authentication.</summary>
    Art11FraudPrevention = 18
}

/// <summary>
/// The safeguard of an international transfer (LGPD art. 33, S52 §4.2), stored as
/// <c>data_catalogue_entries.transfer_mechanism</c> (int, CHECK 1–12).
/// </summary>
public enum InternationalTransferMechanism
{
    /// <summary>Art. 33, I — a country or organisation with an adequate level of protection.</summary>
    AdequateCountry = 1,

    /// <summary>Art. 33, II, a — specific contractual clauses for the transfer.</summary>
    SpecificContractualClauses = 2,

    /// <summary>Art. 33, II, b — standard contractual clauses.</summary>
    StandardContractualClauses = 3,

    /// <summary>Art. 33, II, c — global corporate rules.</summary>
    GlobalCorporateRules = 4,

    /// <summary>Art. 33, II, d — seals, certificates and codes of conduct.</summary>
    SealsCertificatesCodes = 5,

    /// <summary>Art. 33, III — international legal cooperation.</summary>
    LegalCooperation = 6,

    /// <summary>Art. 33, IV — protecting life or physical safety.</summary>
    ProtectionOfLife = 7,

    /// <summary>Art. 33, V — authorised by the ANPD.</summary>
    AnpdAuthorization = 8,

    /// <summary>Art. 33, VI — a commitment in an international cooperation agreement.</summary>
    InternationalCommitment = 9,

    /// <summary>Art. 33, VII — a public policy or a legal attribution of the public service.</summary>
    PublicPolicy = 10,

    /// <summary>Art. 33, VIII — the data subject's specific and highlighted consent to the transfer.</summary>
    SpecificConsent = 11,

    /// <summary>Art. 33, IX — the cases of art. 7º, II, V and VI.</summary>
    Article7Basis = 12
}

/// <summary>
/// Why the data is in a country (S52 §4.2), stored as <c>data_catalogue_locations.purpose</c>. The same vocabulary as a
/// supplier's <see cref="ThirdPartyDataLocationPurpose"/>, so the record's own locations and its processors' read alike.
/// </summary>
public enum DataLocationPurpose
{
    /// <summary>Where the data is stored at rest.</summary>
    Storage = 1,

    /// <summary>Where it is processed.</summary>
    Processing = 2,

    /// <summary>Where its backups are kept.</summary>
    Backup = 3,

    /// <summary>From where support staff can reach it.</summary>
    SupportAccess = 4
}

/// <summary>
/// The kind of a catalogued legal or contractual requirement (S52 §4.1), stored as <c>legal_requirements.kind</c> (CHECK
/// 1–4). Only a contract may name a third party as its counterparty.
/// </summary>
public enum LegalRequirementKind
{
    /// <summary>A law (the LGPD, the Marco Civil, a sector law).</summary>
    Law = 1,

    /// <summary>A regulation, resolution or normative act (an ANPD resolution, a regulator's rule).</summary>
    Regulation = 2,

    /// <summary>A contract or a clause of one.</summary>
    Contract = 3,

    /// <summary>An internal norm or policy of the organisation.</summary>
    InternalNorm = 4
}

/// <summary>
/// Where a data protection impact assessment (RIPD) stands (S52 §4.4), stored as <c>dpias.status</c> (CHECK 1–3). An
/// approved one is frozen — reviewing it is a new one, retiring this — and none is ever deleted (S52 D12).
/// </summary>
public enum DpiaStatus
{
    /// <summary>Being written; editable and linkable.</summary>
    Draft = 1,

    /// <summary>Approved by a person (never the third line); frozen.</summary>
    Approved = 2,

    /// <summary>Retired with a reason; kept as evidence, counts for nothing.</summary>
    Retired = 3
}

/// <summary>The residual risk to the data subjects a RIPD concludes (S52 §4.4), stored as <c>dpias.residual_risk</c>.</summary>
public enum DpiaResidualRisk
{
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>
/// What a RIPD covers (S52 §4.4, T208), stored as <c>dpia_links.kind</c> and derived from the definition of the linked
/// entity, never taken from the payload.
/// </summary>
public enum DpiaLinkKind
{
    /// <summary>A catalogued data record (<c>organizationData</c>).</summary>
    DataRecord = 1,

    /// <summary>A business process (<c>businessProcess</c>) the processing serves.</summary>
    BusinessProcess = 2
}
