using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.DataCatalogue;
using Tools.DataCatalogue;
using Xunit;

namespace Tools.Tests.DataCatalogue;

/// <summary>
/// Stage 9.11 (S52 §4.6, §8 F1–F11) — the gaps in the LGPD catalogue of a data record. The two edge cases the methodology
/// names are F2 and F6 (T210): sensitive data with no declared legal basis is a finding, never compliant by omission, and an
/// expired retention signals — the evaluator is pure and has nothing it could delete with.
/// </summary>
[TestSubject(typeof(DataCatalogueFindingsEvaluator))]
public class DataCatalogueFindingsEvaluatorTest
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private static PurposeFacts Purpose(string purpose, LgpdLegalBasis? basis, int? requirement = null,
        string? reference = "GED 2026/12") => new(purpose, basis, requirement, reference);

    private static DpiaFacts Approved(int id = 1, DpiaResidualRisk risk = DpiaResidualRisk.Low, DateTime? review = null) =>
        new(id, DpiaStatus.Approved, risk, review ?? Now.AddYears(1));

    /// <summary>Sensitive health data with everything declared and nothing wrong.</summary>
    private static DataRecordFacts Documented() => new(
        true, PersonalDataCategory.SensitivePersonal, false, false, false,
        [Purpose("Student health care", LgpdLegalBasis.Art11HealthProtection)],
        60, Now.AddYears(2), ["BR"], [], false, null, [Approved()]);

    private static List<DataCatalogueFindingCode> Codes(DataRecordFacts facts) =>
        DataCatalogueFindingsEvaluator.Evaluate(facts, Now).Select(f => f.Code).ToList();

    /// <summary>F1 — a documented sensitive record has no finding.</summary>
    [Fact]
    public void TestF1_ADocumentedSensitiveRecordHasNoFinding() => Assert.Empty(Codes(Documented()));

    /// <summary>
    /// F2 (T210) — sensitive personal data with no declared legal basis is a finding: with no purpose at all, with a purpose
    /// whose basis is not declared, and with one purpose of two undeclared. It is never read as compliant by omission.
    /// </summary>
    [Fact]
    public void TestF2_SensitiveDataWithNoDeclaredLegalBasisIsAFinding()
    {
        var noPurpose = Documented() with { Purposes = [] };
        var noBasis = Documented() with { Purposes = [Purpose("Student health care", null)] };
        var oneOfTwo = Documented() with
        {
            Purposes = [Purpose("Student health care", LgpdLegalBasis.Art11HealthProtection), Purpose("Research", null)]
        };

        Assert.Equal([DataCatalogueFindingCode.PurposeMissing, DataCatalogueFindingCode.LegalBasisMissing], Codes(noPurpose));
        Assert.Equal([DataCatalogueFindingCode.LegalBasisMissing], Codes(noBasis));
        Assert.Equal([DataCatalogueFindingCode.LegalBasisMissing], Codes(oneOfTwo));

        var finding = DataCatalogueFindingsEvaluator.Evaluate(oneOfTwo, Now).Single();
        Assert.Contains("sensitive", finding.Message);
        Assert.Contains("art. 11", finding.Message);
        Assert.Contains("Research", finding.Message);

        // The same gap on ordinary personal data is the same finding, citing art. 7º.
        var personal = noBasis with { PersonalData = PersonalDataCategory.Personal };
        Assert.Contains("art. 7º", DataCatalogueFindingsEvaluator.Evaluate(personal, Now)
            .Single(f => f.Code == DataCatalogueFindingCode.LegalBasisMissing).Message);
    }

    /// <summary>F3 — sensitive data under a basis of art. 7º is a finding; every basis of art. 11 is not.</summary>
    [Theory]
    [InlineData(LgpdLegalBasis.Art7LegitimateInterest, true)]
    [InlineData(LgpdLegalBasis.Art7CreditProtection, true)]
    [InlineData(LgpdLegalBasis.Art7Contract, true)]
    [InlineData(LgpdLegalBasis.Art7Consent, true)]
    [InlineData(LgpdLegalBasis.Art11Consent, false)]
    [InlineData(LgpdLegalBasis.Art11ExerciseOfRights, false)]
    [InlineData(LgpdLegalBasis.Art11FraudPrevention, false)]
    public void TestF3_SensitiveDataNeedsAnArticle11Basis(LgpdLegalBasis basis, bool finding)
    {
        var facts = Documented() with { Purposes = [Purpose("Enrolment", basis)] };
        Assert.Equal(finding, Codes(facts).Contains(DataCatalogueFindingCode.LegalBasisNotValidForSensitiveData));

        // An art. 7º basis is fine for ordinary personal data.
        Assert.DoesNotContain(DataCatalogueFindingCode.LegalBasisNotValidForSensitiveData,
            Codes(facts with { PersonalData = PersonalDataCategory.Personal }));
    }

    /// <summary>F4 — an uncatalogued record is a finding of its own and nothing else; an undeclared category is a finding.</summary>
    [Fact]
    public void TestF4_UncataloguedAndUndeclaredAreFindings()
    {
        Assert.Equal([DataCatalogueFindingCode.NotCatalogued], Codes(DataRecordFacts.Uncatalogued));

        var undeclared = Documented() with { PersonalData = null };
        Assert.Equal([DataCatalogueFindingCode.PersonalDataUndeclared], Codes(undeclared));
    }

    /// <summary>F5 — non-personal and anonymised data owe nothing to the LGPD; only an expired retention reaches them.</summary>
    [Theory]
    [InlineData(PersonalDataCategory.NotPersonal)]
    [InlineData(PersonalDataCategory.Anonymised)]
    public void TestF5_NonPersonalDataOnlyExpiresItsRetention(PersonalDataCategory category)
    {
        var bare = new DataRecordFacts(true, category, null, true, true, [], null, null, ["US"], [], null, null, []);
        Assert.Empty(Codes(bare));

        Assert.Equal([DataCatalogueFindingCode.RetentionExpired], Codes(bare with { RetentionReviewDueAt = Now.AddDays(-1) }));
    }

    /// <summary>
    /// F6 (T210) — a retention review date in the past signals, with the date and the sentence that nothing is deleted; due
    /// now or later does not. The evaluator only returns findings: there is nothing in it that could delete.
    /// </summary>
    [Fact]
    public void TestF6_AnExpiredRetentionSignals()
    {
        var expired = Documented() with { RetentionReviewDueAt = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc) };

        var finding = Assert.Single(DataCatalogueFindingsEvaluator.Evaluate(expired, Now));
        Assert.Equal(DataCatalogueFindingCode.RetentionExpired, finding.Code);
        Assert.Contains("2026-01-31", finding.Message);
        Assert.Contains("Nothing is deleted", finding.Message);

        Assert.Empty(Codes(Documented() with { RetentionReviewDueAt = Now }));
        Assert.Empty(Codes(Documented() with { RetentionReviewDueAt = Now.AddDays(1) }));
    }

    /// <summary>
    /// F7 — international transfer: a country outside Brazil, the record's own or a processor's, needs a declared transfer;
    /// a transfer declared false and contradicted is the same finding; a declared transfer needs its safeguard; Brazil only
    /// is no transfer.
    /// </summary>
    [Fact]
    public void TestF7_InternationalTransfer()
    {
        Assert.Equal([DataCatalogueFindingCode.InternationalTransferUndeclared],
            Codes(Documented() with { OwnCountries = ["BR", "us"], InternationalTransfer = null }));

        var viaProcessor = Documented() with { ProcessorCountries = ["IE", "BR"], InternationalTransfer = false };
        var contradicted = DataCatalogueFindingsEvaluator.Evaluate(viaProcessor, Now).Single();
        Assert.Equal(DataCatalogueFindingCode.InternationalTransferUndeclared, contradicted.Code);
        Assert.Contains("declared not to leave Brazil", contradicted.Message);
        Assert.Contains("IE", contradicted.Message);

        Assert.Equal([DataCatalogueFindingCode.TransferMechanismMissing],
            Codes(viaProcessor with { InternationalTransfer = true }));
        Assert.Empty(Codes(viaProcessor with
        {
            InternationalTransfer = true, TransferMechanism = InternationalTransferMechanism.StandardContractualClauses
        }));

        Assert.Empty(Codes(Documented() with { OwnCountries = ["BR"], ProcessorCountries = ["br"] }));
        Assert.Equal(["IE", "US"], DataCatalogueFindingsEvaluator.OutsideHome(["us", "BR", " ie ", "US", null]));
    }

    /// <summary>
    /// F8 — the RIPD: high-risk processing (sensitive, minors, large volume) needs an approved one — a draft or a retired
    /// one does not count —; an approved one past its review or concluding a high residual risk is a finding.
    /// </summary>
    [Fact]
    public void TestF8_TheRipd()
    {
        Assert.Equal([DataCatalogueFindingCode.DpiaMissing], Codes(Documented() with { Dpias = [] }));
        Assert.Equal([DataCatalogueFindingCode.DpiaMissing], Codes(Documented() with
        {
            Dpias = [new DpiaFacts(2, DpiaStatus.Draft, DpiaResidualRisk.Low, null),
                new DpiaFacts(3, DpiaStatus.Retired, DpiaResidualRisk.Low, null)]
        }));

        var personal = Documented() with
        {
            PersonalData = PersonalDataCategory.Personal, Purposes = [Purpose("Enrolment", LgpdLegalBasis.Art7Contract)],
            Dpias = []
        };
        Assert.Empty(Codes(personal));
        Assert.Equal([DataCatalogueFindingCode.DpiaMissing], Codes(personal with { InvolvesMinors = true }));
        Assert.Equal([DataCatalogueFindingCode.DpiaMissing], Codes(personal with { LargeVolume = true }));

        Assert.Equal([DataCatalogueFindingCode.DpiaReviewOverdue],
            Codes(Documented() with { Dpias = [Approved(7, review: Now.AddDays(-1))] }));
        Assert.Equal([DataCatalogueFindingCode.DpiaResidualRiskHigh],
            Codes(Documented() with { Dpias = [Approved(8, DpiaResidualRisk.High)] }));
    }

    /// <summary>F9 — a legal obligation that cites no requirement, and a legitimate interest with no balancing test.</summary>
    [Fact]
    public void TestF9_UnnamedObligationAndUnassessedLegitimateInterest()
    {
        var personal = Documented() with { PersonalData = PersonalDataCategory.Personal, Dpias = [] };

        Assert.Equal([DataCatalogueFindingCode.LegalObligationUnnamed],
            Codes(personal with { Purposes = [Purpose("Tax records", LgpdLegalBasis.Art7LegalObligation)] }));
        Assert.Empty(Codes(personal with { Purposes = [Purpose("Tax records", LgpdLegalBasis.Art7LegalObligation, 4)] }));
        Assert.Equal([DataCatalogueFindingCode.LegalObligationUnnamed],
            Codes(Documented() with { Purposes = [Purpose("Health census", LgpdLegalBasis.Art11LegalObligation)] }));

        Assert.Equal([DataCatalogueFindingCode.LegitimateInterestUnassessed],
            Codes(personal with { Purposes = [Purpose("Alumni news", LgpdLegalBasis.Art7LegitimateInterest, reference: " ")] }));
        Assert.Empty(Codes(personal with { Purposes = [Purpose("Alumni news", LgpdLegalBasis.Art7LegitimateInterest)] }));
    }

    /// <summary>F10 — personal data with neither a retention period nor a review date, or with no location at all.</summary>
    [Fact]
    public void TestF10_RetentionAndLocationUndeclared()
    {
        Assert.Equal([DataCatalogueFindingCode.RetentionUndeclared],
            Codes(Documented() with { RetentionPeriodMonths = null, RetentionReviewDueAt = null }));
        Assert.Empty(Codes(Documented() with { RetentionPeriodMonths = null }));
        Assert.Empty(Codes(Documented() with { RetentionReviewDueAt = null }));

        Assert.Equal([DataCatalogueFindingCode.LocationUndeclared], Codes(Documented() with { OwnCountries = [] }));
        Assert.Empty(Codes(Documented() with { OwnCountries = [], ProcessorCountries = ["BR"] }));
    }

    /// <summary>F11 — a bare sensitive record lists its findings in code order, every time.</summary>
    [Fact]
    public void TestF11_TheOrderIsStable()
    {
        var bare = new DataRecordFacts(true, PersonalDataCategory.SensitivePersonal, true, true, true,
            [Purpose("Alumni news", LgpdLegalBasis.Art7LegitimateInterest, reference: null),
                Purpose("Tax", LgpdLegalBasis.Art7LegalObligation)],
            null, Now.AddDays(-3), [], ["US"], true, null, [Approved(9, DpiaResidualRisk.High, Now.AddDays(-2))]);

        var codes = Codes(bare);
        Assert.Equal(codes.OrderBy(c => (int)c), codes);
        Assert.Equal(
        [
            DataCatalogueFindingCode.LegalBasisNotValidForSensitiveData, DataCatalogueFindingCode.LegalObligationUnnamed,
            DataCatalogueFindingCode.LegitimateInterestUnassessed, DataCatalogueFindingCode.RetentionExpired,
            DataCatalogueFindingCode.TransferMechanismMissing, DataCatalogueFindingCode.DpiaReviewOverdue,
            DataCatalogueFindingCode.DpiaResidualRiskHigh
        ], codes);
        Assert.Equal(codes, Codes(bare));
    }

    /// <summary>High risk is personal data that is sensitive, of minors or in large volume — never non-personal data.</summary>
    [Fact]
    public void TestHighRiskIsPersonalData()
    {
        Assert.True(DataCatalogueFindingsEvaluator.IsHighRisk(PersonalDataCategory.SensitivePersonal, null, false));
        Assert.True(DataCatalogueFindingsEvaluator.IsHighRisk(PersonalDataCategory.Personal, true, false));
        Assert.True(DataCatalogueFindingsEvaluator.IsHighRisk(PersonalDataCategory.Personal, false, true));
        Assert.False(DataCatalogueFindingsEvaluator.IsHighRisk(PersonalDataCategory.Personal, false, false));
        Assert.False(DataCatalogueFindingsEvaluator.IsHighRisk(PersonalDataCategory.NotPersonal, true, true));
        Assert.False(DataCatalogueFindingsEvaluator.IsHighRisk(null, true, true));
    }
}
