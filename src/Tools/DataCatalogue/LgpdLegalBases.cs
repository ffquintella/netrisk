using DAL.Enums;

namespace Tools.DataCatalogue;

/// <summary>
/// The LGPD legal bases (Stage 9.11, S52 §4.3): which article and inciso each one is, and the three questions the findings
/// ask of a basis — is it one sensitive data admits (art. 11), is it a legal obligation (which should cite the obligation),
/// is it legitimate interest (which should cite its balancing test).
/// </summary>
public static class LgpdLegalBases
{
    private static readonly IReadOnlyDictionary<LgpdLegalBasis, string> Articles = new Dictionary<LgpdLegalBasis, string>
    {
        [LgpdLegalBasis.Art7Consent] = "LGPD art. 7º, I",
        [LgpdLegalBasis.Art7LegalObligation] = "LGPD art. 7º, II",
        [LgpdLegalBasis.Art7PublicPolicy] = "LGPD art. 7º, III",
        [LgpdLegalBasis.Art7Research] = "LGPD art. 7º, IV",
        [LgpdLegalBasis.Art7Contract] = "LGPD art. 7º, V",
        [LgpdLegalBasis.Art7ExerciseOfRights] = "LGPD art. 7º, VI",
        [LgpdLegalBasis.Art7ProtectionOfLife] = "LGPD art. 7º, VII",
        [LgpdLegalBasis.Art7HealthProtection] = "LGPD art. 7º, VIII",
        [LgpdLegalBasis.Art7LegitimateInterest] = "LGPD art. 7º, IX",
        [LgpdLegalBasis.Art7CreditProtection] = "LGPD art. 7º, X",
        [LgpdLegalBasis.Art11Consent] = "LGPD art. 11, I",
        [LgpdLegalBasis.Art11LegalObligation] = "LGPD art. 11, II, a",
        [LgpdLegalBasis.Art11PublicPolicy] = "LGPD art. 11, II, b",
        [LgpdLegalBasis.Art11Research] = "LGPD art. 11, II, c",
        [LgpdLegalBasis.Art11ExerciseOfRights] = "LGPD art. 11, II, d",
        [LgpdLegalBasis.Art11ProtectionOfLife] = "LGPD art. 11, II, e",
        [LgpdLegalBasis.Art11HealthProtection] = "LGPD art. 11, II, f",
        [LgpdLegalBasis.Art11FraudPrevention] = "LGPD art. 11, II, g"
    };

    /// <summary>The citation of a basis: "LGPD art. 11, II, a". Throws for an undefined value.</summary>
    public static string ArticleOf(LgpdLegalBasis basis) =>
        Articles.TryGetValue(basis, out var article)
            ? article
            : throw new ArgumentOutOfRangeException(nameof(basis), basis, "Not an LGPD legal basis.");

    /// <summary>A basis of art. 11 — the only ones sensitive personal data admits.</summary>
    public static bool IsArticle11(LgpdLegalBasis basis) =>
        basis is >= LgpdLegalBasis.Art11Consent and <= LgpdLegalBasis.Art11FraudPrevention;

    /// <summary>Compliance with a legal or regulatory obligation, under either article.</summary>
    public static bool IsLegalObligation(LgpdLegalBasis basis) =>
        basis is LgpdLegalBasis.Art7LegalObligation or LgpdLegalBasis.Art11LegalObligation;

    /// <summary>Legitimate interest (art. 7º, IX) — which art. 11 does not admit.</summary>
    public static bool IsLegitimateInterest(LgpdLegalBasis basis) => basis == LgpdLegalBasis.Art7LegitimateInterest;
}
