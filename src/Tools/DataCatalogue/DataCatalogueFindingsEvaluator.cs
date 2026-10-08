using System.Globalization;
using DAL.Enums;
using Model.DataCatalogue;

namespace Tools.DataCatalogue;

/// <summary>One purpose of a data record, as the findings read it.</summary>
public sealed record PurposeFacts(string Purpose, LgpdLegalBasis? LegalBasis, int? LegalRequirementId, string? BasisReference);

/// <summary>A RIPD linked to a data record, as the findings read it.</summary>
public sealed record DpiaFacts(int Id, DpiaStatus Status, DpiaResidualRisk? ResidualRisk, DateTime? NextReviewDueAt);

/// <summary>
/// What the findings read of a data record (S52 §4.6). <paramref name="OwnCountries"/> are the record's own locations;
/// <paramref name="ProcessorCountries"/> the locations of its live processors and their sub-processors' countries, all
/// countries included — the evaluator decides which are outside Brazil.
/// </summary>
public sealed record DataRecordFacts(
    bool Catalogued,
    PersonalDataCategory? PersonalData,
    bool? InvolvesMinors,
    bool LargeVolume,
    bool StrategicResearch,
    IReadOnlyList<PurposeFacts> Purposes,
    int? RetentionPeriodMonths,
    DateTime? RetentionReviewDueAt,
    IReadOnlyCollection<string> OwnCountries,
    IReadOnlyCollection<string> ProcessorCountries,
    bool? InternationalTransfer,
    InternationalTransferMechanism? TransferMechanism,
    IReadOnlyList<DpiaFacts> Dpias)
{
    /// <summary>A record that has no catalogue at all.</summary>
    public static DataRecordFacts Uncatalogued { get; } = new(false, null, null, false, false, [], null, null, [], [], null,
        null, []);
}

/// <summary>
/// The gaps in the LGPD catalogue of a data record (Stage 9.11, S52 §4.6, T207, T210): whether it is catalogued at all,
/// whether personal data is declared, each purpose's legal basis — and whether sensitive data rests on a basis art. 11
/// admits —, retention, location, international transfer and the RIPD.
///
/// Two rules shape it. <b>Absent is never compliant</b> (S52 D4): an uncatalogued record, an undeclared category, sensitive
/// data with no declared legal basis are findings of their own, never a pass by omission. <b>A finding is a signal</b>
/// (S52 D3, D5): nothing here refuses or deletes anything — an expired retention says so, and eliminating the data is the
/// controller's decision in the system that holds it. Computed on read; pure.
/// </summary>
public static class DataCatalogueFindingsEvaluator
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Whether the category is one the LGPD protects: personal or sensitive personal data.</summary>
    public static bool IsPersonal(PersonalDataCategory? category) =>
        category is PersonalDataCategory.Personal or PersonalDataCategory.SensitivePersonal;

    /// <summary>
    /// High-risk processing for which a RIPD is expected: personal data that is sensitive, of children or adolescents, or
    /// in large volume — an <em>interpretation</em> of Resolução CD/ANPD nº 2/2022 art. 4º, for the DPO to confirm (S52 §11).
    /// </summary>
    public static bool IsHighRisk(PersonalDataCategory? category, bool? involvesMinors, bool largeVolume) =>
        IsPersonal(category) &&
        (category == PersonalDataCategory.SensitivePersonal || involvesMinors == true || largeVolume);

    /// <summary>The countries outside Brazil among <paramref name="countries"/>, upper-cased, distinct and ordered.</summary>
    public static List<string> OutsideHome(IEnumerable<string?> countries) =>
        countries
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim().ToUpperInvariant())
            .Where(c => c != DataCatalogueLimits.HomeCountry)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

    public static List<DataCatalogueFindingDto> Evaluate(DataRecordFacts facts, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var findings = new List<DataCatalogueFindingDto>();
        void Add(DataCatalogueFindingCode code, string message) =>
            findings.Add(new DataCatalogueFindingDto { Code = code, Message = message });

        if (!facts.Catalogued)
        {
            Add(DataCatalogueFindingCode.NotCatalogued,
                "This data record has no LGPD catalogue: whether it holds personal data, why and under which legal basis is " +
                "unknown — which is not the same as holding none.");
            return findings;
        }

        var personal = IsPersonal(facts.PersonalData);
        var sensitive = facts.PersonalData == PersonalDataCategory.SensitivePersonal;

        if (facts.PersonalData is null)
            Add(DataCatalogueFindingCode.PersonalDataUndeclared, "Whether this data record holds personal data is not declared.");

        if (personal)
        {
            if (facts.Purposes.Count == 0)
                Add(DataCatalogueFindingCode.PurposeMissing, "It holds personal data and declares no purpose.");

            var unbased = facts.Purposes.Where(p => p.LegalBasis is null).Select(p => p.Purpose).ToList();
            if (facts.Purposes.Count == 0 || unbased.Count > 0)
            {
                var which = unbased.Count == 0 ? "no purpose declares one" : $"no legal basis for: {Join(unbased)}";
                Add(DataCatalogueFindingCode.LegalBasisMissing, sensitive
                    ? $"It holds sensitive personal data and {which} — LGPD art. 11 requires one for every purpose."
                    : $"It holds personal data and {which} — LGPD art. 7º requires one for every purpose.");
            }

            if (sensitive)
            {
                var article7 = facts.Purposes
                    .Where(p => p.LegalBasis is { } b && !LgpdLegalBases.IsArticle11(b))
                    .Select(p => $"{p.Purpose} ({LgpdLegalBases.ArticleOf(p.LegalBasis!.Value)})")
                    .ToList();
                if (article7.Count > 0)
                    Add(DataCatalogueFindingCode.LegalBasisNotValidForSensitiveData,
                        $"Sensitive personal data rests on a basis of art. 7º, which art. 11 does not admit: {Join(article7)}.");
            }

            var unnamed = facts.Purposes
                .Where(p => p.LegalBasis is { } b && LgpdLegalBases.IsLegalObligation(b) && p.LegalRequirementId is null)
                .Select(p => p.Purpose).ToList();
            if (unnamed.Count > 0)
                Add(DataCatalogueFindingCode.LegalObligationUnnamed,
                    $"A purpose rests on a legal obligation that cites no catalogued requirement: {Join(unnamed)}.");

            var unassessed = facts.Purposes
                .Where(p => p.LegalBasis is { } b && LgpdLegalBases.IsLegitimateInterest(b) &&
                            string.IsNullOrWhiteSpace(p.BasisReference))
                .Select(p => p.Purpose).ToList();
            if (unassessed.Count > 0)
                Add(DataCatalogueFindingCode.LegitimateInterestUnassessed,
                    $"A purpose rests on legitimate interest with no reference to its balancing test: {Join(unassessed)}.");

            if (facts.RetentionPeriodMonths is null && facts.RetentionReviewDueAt is null)
                Add(DataCatalogueFindingCode.RetentionUndeclared,
                    "It holds personal data and declares neither a retention period nor a retention review date.");
        }

        // Every catalogued record: the date the controller set has passed. A signal — nothing is deleted here, ever (D5).
        if (facts.RetentionReviewDueAt is { } due && due < now)
            Add(DataCatalogueFindingCode.RetentionExpired,
                $"The retention review was due on {due.ToString("yyyy-MM-dd", Invariant)}. Nothing is deleted: reviewing or " +
                "eliminating the data is the controller's decision, in the system that holds it.");

        if (personal)
        {
            if (facts.OwnCountries.Count == 0 && facts.ProcessorCountries.Count == 0)
                Add(DataCatalogueFindingCode.LocationUndeclared,
                    "It holds personal data and no location is declared — neither its own nor a processor's.");

            var abroad = OutsideHome(facts.OwnCountries.Concat(facts.ProcessorCountries));
            if (abroad.Count > 0 && facts.InternationalTransfer != true)
                Add(DataCatalogueFindingCode.InternationalTransferUndeclared, facts.InternationalTransfer == false
                    ? $"It is declared not to leave Brazil, and it reaches {Join(abroad)} — through its own locations or a processor's."
                    : $"It reaches {Join(abroad)} — through its own locations or a processor's — and no international transfer is declared.");

            if (facts.InternationalTransfer == true && facts.TransferMechanism is null)
                Add(DataCatalogueFindingCode.TransferMechanismMissing,
                    "An international transfer is declared without its safeguard (LGPD art. 33).");
        }

        var approved = facts.Dpias.Where(d => d.Status == DpiaStatus.Approved).ToList();

        if (IsHighRisk(facts.PersonalData, facts.InvolvesMinors, facts.LargeVolume) && approved.Count == 0)
            Add(DataCatalogueFindingCode.DpiaMissing,
                "High-risk processing — sensitive data, data of children or adolescents, or large volume — with no approved " +
                "RIPD (DPIA) covering it; a draft or a retired one does not count.");

        var overdue = approved.Where(d => d.NextReviewDueAt is { } at && at < now).Select(d => d.Id).ToList();
        if (overdue.Count > 0)
            Add(DataCatalogueFindingCode.DpiaReviewOverdue,
                $"An approved RIPD covering it is past its review date: #{string.Join(", #", overdue)}.");

        var high = approved.Where(d => d.ResidualRisk == DpiaResidualRisk.High).Select(d => d.Id).ToList();
        if (high.Count > 0)
            Add(DataCatalogueFindingCode.DpiaResidualRiskHigh,
                $"An approved RIPD covering it concluded a high residual risk to the data subjects: #{string.Join(", #", high)}.");

        return findings;
    }

    private static string Join(IEnumerable<string> items) => string.Join("; ", items);
}
