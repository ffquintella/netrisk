using DAL.Enums;

namespace Model.RiskFlags;

/// <summary>
/// The eleven mandatory flags of MIGR-TI/IA Phase 2 and the Gate A condition "no legitimate acceptance",
/// with the origin Stage 9.5 declares for each (S46 §4.1).
///
/// Lives in <c>Model</c>, like <see cref="Notifications.NotificationCatalog"/>, so the API, the desktop
/// client and the tests read one list: a client with its own copy could offer to declare a flag the
/// server does not know, or show the wrong origin.
/// </summary>
public static class RiskFlagCatalogue
{
    /// <summary>The longest declaration or withdrawal reason (<c>risk_flags.declared_reason</c>).</summary>
    public const int MaxReasonLength = 1000;

    /// <summary>The longest decision reason accepted from a person.</summary>
    public const int MaxDecisionReasonLength = 2000;

    public static readonly IReadOnlyList<RiskFlagDescriptor> Flags =
    [
        new(RiskFlagCode.HumanSafety, 1, "Life, health or human safety",
            "The scenario can harm a person's life, health or safety.",
            RiskFlagDerivation.None, true, null),
        new(RiskFlagCode.LegalRegulatory, 2, "Legal or regulatory obligation, or LGPD",
            "The scenario breaches, or risks breaching, a legal, regulatory or data-protection obligation.",
            RiskFlagDerivation.None, true, "Stage 9.11 (LGPD data catalogue)"),
        new(RiskFlagCode.KnownExploitation, 3, "Known exploitation (CISA KEV) or active attack",
            "An open finding linked to the risk has a CVE listed in CISA KEV, or an active attack is declared.",
            RiskFlagDerivation.Kev, true, null),
        new(RiskFlagCode.CriticalProcessContinuity, 4, "Critical process with RTO/RPO threatened",
            "The risk reaches a critical business process whose recovery objectives are threatened.",
            RiskFlagDerivation.Bia, false, null),
        new(RiskFlagCode.SensitiveData, 5, "Sensitive personal data, large volume or strategic research",
            "The risk is linked to data classified at a level marked sensitive, or this is declared.",
            RiskFlagDerivation.DataClassification, false, "Stage 9.11 (LGPD data catalogue)"),
        new(RiskFlagCode.SystemicSinglePointOfFailure, 6, "Systemic risk or single point of failure",
            "The scenario is systemic or rests on a single point of failure.",
            RiskFlagDerivation.None, false, null),
        new(RiskFlagCode.ThirdPartyConcentration, 7, "Concentration in a third party, cloud or identity",
            "The scenario concentrates on one supplier, cloud or identity provider.",
            RiskFlagDerivation.None, false, "Stage 9.10 (third-party register)"),
        new(RiskFlagCode.LowProbabilityCatastrophic, 8, "Low probability, catastrophic impact",
            "A rare event whose impact would be catastrophic: the inherent run loses in few years, and a loss year is catastrophic.",
            RiskFlagDerivation.TailStatistics, false, null),
        new(RiskFlagCode.EmergingRapidGrowth, 9, "Emerging risk or rapid growth",
            "The risk is new or growing quickly.",
            RiskFlagDerivation.None, false, "Stage 9.8 (KRIs)"),
        new(RiskFlagCode.HighUncertainty, 10, "High uncertainty or weak evidence",
            "The analysis rests on high uncertainty or weak evidence; evidence confidence sits beside it.",
            RiskFlagDerivation.None, false, null),
        new(RiskFlagCode.ArtificialIntelligence, 11, "AI risk: discrimination, hallucination, prompt injection, drift",
            "The scenario involves an AI component. Declarable only until Stage 9.12 makes it derivable.",
            RiskFlagDerivation.None, false, "Stage 9.12 (AI model inventory)")
    ];

    /// <summary>The Phase 4 Gate A condition that is not one of the eleven flags (S46 D6).</summary>
    public static readonly RiskFlagDescriptor NoLegitimateAcceptance =
        new(RiskFlagCode.NoLegitimateAcceptance, null, "Risk without legitimate acceptance",
            "Nobody can legitimately accept this risk — a Gate A condition of Phase 4, declared by an assessor.",
            RiskFlagDerivation.None, true, null);

    /// <summary>The eleven flags and the Gate A condition, in code order.</summary>
    public static IReadOnlyList<RiskFlagDescriptor> All { get; } = [.. Flags, NoLegitimateAcceptance];

    /// <summary>The descriptor of a code, or null when the code is not defined.</summary>
    public static RiskFlagDescriptor? Find(RiskFlagCode code) => All.FirstOrDefault(d => d.Code == code);

    /// <summary>True when the code is one of the twelve this catalogue declares.</summary>
    public static bool IsDefined(RiskFlagCode code) => Find(code) is not null;
}

/// <summary>One catalogue row (S46 §4.1, §6 <c>GET /RiskFlags/Catalogue</c>).</summary>
/// <param name="Code">The stored code.</param>
/// <param name="Number">The methodology's number, 1–11; null for the Gate A condition that is not a flag.</param>
/// <param name="Name">The methodology's name, in English.</param>
/// <param name="Description">What setting it means.</param>
/// <param name="Derivation">Where its derived half comes from in this stage; <see cref="RiskFlagDerivation.None"/> when declared only.</param>
/// <param name="NonDiscretionary">True when it is a Gate A condition.</param>
/// <param name="DerivableIn">The later stage that is expected to derive it, when one is.</param>
public record RiskFlagDescriptor(
    RiskFlagCode Code,
    int? Number,
    string Name,
    string Description,
    RiskFlagDerivation Derivation,
    bool NonDiscretionary,
    string? DerivableIn)
{
    /// <summary>Every flag is declarable, the derived ones included (S46 §4.1).</summary>
    public bool Declarable => true;
}
