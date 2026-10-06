using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using DAL.Enums;
using GUIClient.Tests.Resources;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Risks.Scenario;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// Stage 9.2 (S42 §8, U1–U7) — the pure half of the scenario, hypothesis and near-miss screens: how an
/// empty field and an undeclared confidence read, when the editor asks for duplicates, how the warning
/// lists them, and that every localization key the helper hands to <c>Localizer[…]</c> exists in all
/// three resource files. Those keys are computed, so <c>LocalizationCoverageTest</c>, which scans
/// <c>Localizer["literal"]</c>, cannot see them — a missing one would render as its own name.
/// </summary>
[TestSubject(typeof(RiskScenarioSummary))]
public class RiskScenarioSummaryTest
{
    private const string NotInformed = "Not informed";

    /// <summary>U1 — an empty or whitespace field reads "not informed", never blank; text is trimmed.</summary>
    [Theory]
    [InlineData(null, NotInformed)]
    [InlineData("", NotInformed)]
    [InlineData("   ", NotInformed)]
    [InlineData("  Ransomware  ", "Ransomware")]
    public void TestU1_AnEmptyFieldReadsNotInformed(string? text, string expected)
    {
        Assert.Equal(expected, RiskScenarioSummary.Field(text, NotInformed));
    }

    /// <summary>U2 — NULL confidence is "not declared", a key of its own, not one of the three levels.</summary>
    [Fact]
    public void TestU2_EachConfidenceHasItsOwnKeyAndNullIsNotDeclared()
    {
        Assert.Equal("EvidenceConfidenceNotDeclared", RiskScenarioSummary.ConfidenceKey(null));
        Assert.Equal("EvidenceConfidenceConfirmed", RiskScenarioSummary.ConfidenceKey(EvidenceConfidence.Confirmed));
        Assert.Equal("EvidenceConfidenceIndicative", RiskScenarioSummary.ConfidenceKey(EvidenceConfidence.Indicative));
        Assert.Equal("EvidenceConfidenceHypothesis", RiskScenarioSummary.ConfidenceKey(EvidenceConfidence.Hypothesis));

        // The editor offers "not declared" first and every level once.
        Assert.Null(RiskScenarioSummary.ConfidenceChoices[0]);
        Assert.Equal(Enum.GetValues<EvidenceConfidence>().Length + 1, RiskScenarioSummary.ConfidenceChoices.Count);
        Assert.Equal(RiskScenarioSummary.ConfidenceChoices.Count, RiskScenarioSummary.ConfidenceChoices.Distinct().Count());
    }

    /// <summary>U3 — the origin and kind keys, incident first because it is the default.</summary>
    [Fact]
    public void TestU3_OriginAndKindKeys()
    {
        Assert.Equal("PendingOriginAssessment", RiskScenarioSummary.OriginKey(PendingRiskOrigin.Assessment));
        Assert.Equal("PendingOriginStandalone", RiskScenarioSummary.OriginKey(PendingRiskOrigin.Standalone));
        Assert.Equal("IncidentKindIncident", RiskScenarioSummary.KindKey(IncidentKind.Incident));
        Assert.Equal("IncidentKindNearMiss", RiskScenarioSummary.KindKey(IncidentKind.NearMiss));
        Assert.Equal(IncidentKind.Incident, RiskScenarioSummary.KindChoices[0]);
        Assert.Equal(Enum.GetValues<IncidentKind>(), RiskScenarioSummary.KindChoices.ToArray());
    }

    /// <summary>
    /// U4 — the editor asks only with both halves; always on create; on edit only when the pair
    /// changed, so re-saving a risk does not re-warn about a duplicate the user already accepted.
    /// </summary>
    [Theory]
    [InlineData(true, null, null, "Portal unavailable", "Enrolments lost", true)]
    [InlineData(true, null, null, "Portal unavailable", null, false)]
    [InlineData(true, null, null, "   ", "Enrolments lost", false)]
    [InlineData(false, "Portal unavailable", "Enrolments lost", "Portal unavailable", "Enrolments lost", false)]
    [InlineData(false, "Portal unavailable", "Enrolments lost", " portal UNAVAILABLE ", "Enrolments lost", false)]
    [InlineData(false, "Portal unavailable", "Enrolments lost", "Portal unavailable", "Reputational damage", true)]
    [InlineData(false, null, null, "Portal unavailable", "Enrolments lost", true)]
    public void TestU4_WhenTheEditorChecksForDuplicates(bool creating, string? originalEvent, string? originalConsequences,
        string? centralEvent, string? consequences, bool expected)
    {
        Assert.Equal(expected, RiskScenarioSummary.ShouldCheckDuplicates(creating, originalEvent,
            originalConsequences, centralEvent, consequences));
    }

    /// <summary>U5 — one line per risk, in the server's order, with its status.</summary>
    [Fact]
    public void TestU5_TheWarningListsEachDuplicateWithItsStatus()
    {
        var lines = RiskScenarioSummary.DuplicateLines(
        [
            new RiskScenarioDuplicate { RiskId = 3, Subject = "Portal outage", Status = "New" },
            new RiskScenarioDuplicate { RiskId = 9, Subject = "Enrolment loss", Status = "Closed" }
        ]);

        Assert.Equal($"#3 Portal outage (New){Environment.NewLine}#9 Enrolment loss (Closed)", lines);
    }

    /// <summary>U6 — a long list is capped with "+n" so the question stays on the dialog.</summary>
    [Fact]
    public void TestU6_ALongListIsCapped()
    {
        var many = Enumerable.Range(1, 8)
            .Select(i => new RiskScenarioDuplicate { RiskId = i, Subject = $"R{i}", Status = "New" });

        var lines = RiskScenarioSummary.DuplicateLines(many, max: 5).Split(Environment.NewLine);

        Assert.Equal(6, lines.Length);
        Assert.Equal("+3", lines[^1]);
        Assert.Equal(string.Empty, RiskScenarioSummary.DuplicateLines(null));
    }

    private static IEnumerable<string> ComputedKeys() =>
        RiskScenarioSummary.ConfidenceChoices.Select(RiskScenarioSummary.ConfidenceKey)
            .Concat(Enum.GetValues<PendingRiskOrigin>().Select(RiskScenarioSummary.OriginKey))
            .Concat(Enum.GetValues<IncidentKind>().Select(RiskScenarioSummary.KindKey))
            .Distinct();

    /// <summary>U7 — every computed key resolves in all three resource files.</summary>
    [Theory]
    [InlineData("Localization.resx")]
    [InlineData("Localization.en-US.resx")]
    [InlineData("Localization.pt-BR.resx")]
    public void TestU7_EveryComputedKeyResolvesIn(string file)
    {
        var declared = XDocument.Load(EntityConfigurationSchema.ResourcePath(file)).Root!
            .Elements("data")
            .Select(d => d.Attribute("name")?.Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ComputedKeys().Where(k => !declared.Contains(k)).ToList();

        Assert.True(missing.Count == 0, $"{file} does not declare: {string.Join(", ", missing)}");
        Assert.Equal(8, ComputedKeys().Count());
    }
}
