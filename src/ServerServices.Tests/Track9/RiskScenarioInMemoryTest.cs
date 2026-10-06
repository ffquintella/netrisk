using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Risks.Scenario;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.2 (S42 §8) — the structured scenario (T150), the evidence confidence (T151), the duplicate
/// warning (T154) and the legacy risk with all four fields NULL (T155), on the real
/// <see cref="RisksService"/> over the in-memory provider.
///
/// Case ids (E, S, C, D) are the ones the specification's test plan names.
/// </summary>
[TestSubject(typeof(RisksService))]
public class RiskScenarioInMemoryTest : InMemoryServiceTestBase
{
    private const int Author = 7;
    private const int UnitA = 100;
    private const int UnitB = 200;

    private IRisksService Risks => GetService<IRisksService>();

    public RiskScenarioInMemoryTest()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = Author, Name = "analyst", Login = "analyst", Enabled = true, Type = "local", Salt = "s",
                Password = Encoding.UTF8.GetBytes("p"), Email = "analyst@x.test"
            });
            ctx.Categories.Add(new Category { Value = 1, Name = "Operational" });
            ctx.Sources.Add(new Source { Value = 1, Name = "Internal" });
            ctx.Entities.Add(NewEntity(UnitA));
            ctx.Entities.Add(NewEntity(UnitB));
        });
    }

    private static Entity NewEntity(int id) => new()
    {
        Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "2.5", Status = "active",
        Created = DateTime.UtcNow, Updated = DateTime.UtcNow
    };

    /// <summary>A risk exactly as one written before Stage 9.2 reads: the five new columns NULL.</summary>
    private static Risk LegacyRisk(int id, int? entityId = null, string status = "New") => new()
    {
        Id = id, Status = status, Subject = $"Legacy risk {id}", ReferenceId = $"R-{id}",
        Assessment = "Free-text assessment", Notes = "Free-text notes",
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        Category = 1, Source = 1, SubmittedBy = Author, EntityId = entityId,
        SubmissionDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Risk NewRisk(string subject, string? centralEvent = null, string? consequences = null) => new()
    {
        Status = "New", Subject = subject, ReferenceId = string.Empty,
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        Category = 1, Source = 1, SubmittedBy = Author,
        ScenarioCentralEvent = centralEvent, ScenarioConsequences = consequences
    };

    /// <summary>A detached copy, as the API deserializes a PUT body — never the tracked row.</summary>
    private Risk Detached(int id)
    {
        using var db = OpenContext();
        return db.Risks.AsNoTracking().Single(r => r.Id == id);
    }

    private void SeedScenario(int id, string? centralEvent, string? consequences, int? entityId = null,
        string status = "New")
    {
        SeedUnscoped(ctx =>
        {
            var risk = LegacyRisk(id, entityId, status);
            risk.ScenarioCentralEvent = centralEvent;
            risk.ScenarioConsequences = consequences;
            ctx.Risks.Add(risk);
        });
    }

    // --- T155: the legacy risk ------------------------------------------------------------------

    /// <summary>
    /// E1 (T155) — a legacy risk with all four scenario fields and the confidence NULL is editable:
    /// a save that changes only the subject succeeds and leaves the five columns NULL. None of them is
    /// required, so nothing the stage added stands between an old record and an ordinary edit.
    /// </summary>
    [Fact]
    public async Task TestE1_ALegacyRiskWithAllFourFieldsNullStaysEditable()
    {
        SeedUnscoped(ctx => ctx.Risks.Add(LegacyRisk(1)));

        var edit = Detached(1);
        Assert.Null(edit.ScenarioCause);
        edit.Subject = "Legacy risk 1, reworded";

        await Risks.SaveRiskAsync(edit);

        var saved = Detached(1);
        Assert.Equal("Legacy risk 1, reworded", saved.Subject);
        Assert.Null(saved.ScenarioCause);
        Assert.Null(saved.ScenarioVulnerability);
        Assert.Null(saved.ScenarioCentralEvent);
        Assert.Null(saved.ScenarioConsequences);
        Assert.Null(saved.EvidenceConfidence);
        Assert.Equal("Free-text notes", saved.Notes);
    }

    /// <summary>E2 (T155) — the legacy risk is listed: the register query does not filter on the new columns.</summary>
    [Fact]
    public async Task TestE2_ALegacyRiskWithAllFourFieldsNullStaysListable()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(LegacyRisk(1));
            var structured = LegacyRisk(2);
            structured.ScenarioCentralEvent = "Ransomware encrypts the file server";
            structured.EvidenceConfidence = EvidenceConfidence.Confirmed;
            ctx.Risks.Add(structured);
        });

        var listed = await Risks.GetAllAsync();

        Assert.Equal(new[] { 1, 2 }, listed.Select(r => r.Id).OrderBy(id => id).ToArray());
        Assert.Null(listed.Single(r => r.Id == 1).EvidenceConfidence);
    }

    /// <summary>
    /// E3 (T155) — the legacy risk is scorable: a scoring row can be created and updated for it, and it
    /// appears in the inherent/residual pairs every list and heatmap reads.
    /// </summary>
    [Fact]
    public async Task TestE3_ALegacyRiskWithAllFourFieldsNullStaysScorable()
    {
        SeedUnscoped(ctx => ctx.Risks.Add(LegacyRisk(1)));

#pragma warning disable CS0618 // the synchronous scoring calls are the ones the desktop client still drives
        Risks.CreateRiskScoring(new RiskScoring { Id = 1, ScoringMethod = 1, ClassicLikelihood = 3, ClassicImpact = 4, CalculatedRisk = 6.4f });

        var scoring = Risks.GetRiskScoring(1);
        scoring.ClassicImpact = 5;
        scoring.CalculatedRisk = 8f;
        Risks.SaveRiskScoring(scoring);
#pragma warning restore CS0618

        var pair = Assert.Single(await Risks.GetScorePairsAsync([1]));
        Assert.Equal(8f, pair.Inherent);
        Assert.Null(Detached(1).ScenarioCentralEvent);
    }

    // --- T150 / T151: the fields themselves -----------------------------------------------------

    /// <summary>S1 — the four fields and the confidence are stored as sent, trimmed.</summary>
    [Fact]
    public async Task TestS1_CreatingARiskStoresTheFourFieldsAndTheConfidence()
    {
        var risk = NewRisk("Payroll fraud");
        risk.ScenarioCause = "  An insider with payroll access  ";
        risk.ScenarioVulnerability = "No dual approval on bank-detail changes";
        risk.ScenarioCentralEvent = "Salary redirected to a fraudulent account";
        risk.ScenarioConsequences = "Financial loss; regulatory reporting";
        risk.EvidenceConfidence = EvidenceConfidence.Indicative;

        var created = await Risks.CreateRiskAsync(risk);

        var saved = Detached(created!.Id);
        Assert.Equal("An insider with payroll access", saved.ScenarioCause);
        Assert.Equal("No dual approval on bank-detail changes", saved.ScenarioVulnerability);
        Assert.Equal("Salary redirected to a fraudulent account", saved.ScenarioCentralEvent);
        Assert.Equal("Financial loss; regulatory reporting", saved.ScenarioConsequences);
        Assert.Equal(EvidenceConfidence.Indicative, saved.EvidenceConfidence);
    }

    /// <summary>
    /// S2 — a blank field is stored as NULL, so a count of risks "with a central event" counts text
    /// rather than whitespace (the coverage analysis measures exactly that, S42 §10).
    /// </summary>
    [Fact]
    public async Task TestS2_ABlankScenarioFieldIsStoredAsNull()
    {
        var risk = NewRisk("Blank fields", centralEvent: "   ", consequences: "\t");
        risk.ScenarioCause = string.Empty;

        var created = await Risks.CreateRiskAsync(risk);

        var saved = Detached(created!.Id);
        Assert.Null(saved.ScenarioCause);
        Assert.Null(saved.ScenarioCentralEvent);
        Assert.Null(saved.ScenarioConsequences);
    }

    /// <summary>S3 — a confidence outside the three levels is refused on create, and nothing is written.</summary>
    [Fact]
    public async Task TestS3_AnUndefinedConfidenceIsRefusedOnCreate()
    {
        var risk = NewRisk("Bad confidence");
        risk.EvidenceConfidence = (EvidenceConfidence)99;

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Risks.CreateRiskAsync(risk));

        Assert.Equal(nameof(Risk.EvidenceConfidence), ex.ParameterName);
        await using var db = OpenContext();
        Assert.Empty(db.Risks);
    }

    /// <summary>S4 — and on save, where the row keeps its previous confidence.</summary>
    [Fact]
    public async Task TestS4_AnUndefinedConfidenceIsRefusedOnSaveAndTheRowIsUnchanged()
    {
        SeedUnscoped(ctx =>
        {
            var risk = LegacyRisk(1);
            risk.EvidenceConfidence = EvidenceConfidence.Hypothesis;
            ctx.Risks.Add(risk);
        });

        var edit = Detached(1);
        edit.EvidenceConfidence = 0;

        await Assert.ThrowsAsync<InvalidParameterException>(() => Risks.SaveRiskAsync(edit));
        Assert.Equal(EvidenceConfidence.Hypothesis, Detached(1).EvidenceConfidence);
    }

    /// <summary>
    /// S5 — raising the confidence from hypothesis to confirmed is a governance change and lands in the
    /// field-level trail: <c>Risk</c> is on the audit allowlist, so the new columns are audited with no
    /// change to the interceptor.
    /// </summary>
    [Fact]
    public async Task TestS5_ChangingTheConfidenceIsRecordedInTheAuditTrail()
    {
        SeedUnscoped(ctx =>
        {
            var risk = LegacyRisk(1);
            risk.EvidenceConfidence = EvidenceConfidence.Hypothesis;
            ctx.Risks.Add(risk);
        });

        var edit = Detached(1);
        edit.EvidenceConfidence = EvidenceConfidence.Confirmed;
        edit.ScenarioCentralEvent = "Ransomware encrypts the file server";
        await Risks.SaveRiskAsync(edit);

        await using var db = OpenContext();
        var trail = db.AuditLogs.Where(a => a.EntityType == nameof(Risk) && a.EntityId == 1).ToList();
        var confidence = Assert.Single(trail, a => a.Field == nameof(Risk.EvidenceConfidence));
        Assert.Equal(nameof(EvidenceConfidence.Hypothesis), confidence.OldValue);
        Assert.Equal(nameof(EvidenceConfidence.Confirmed), confidence.NewValue);
        Assert.Contains(trail, a => a.Field == nameof(Risk.ScenarioCentralEvent));
    }

    // --- T154: the duplicate warning ------------------------------------------------------------

    /// <summary>
    /// D1 — the same (central event, consequences) pair is a duplicate whatever its case, accents,
    /// spacing or trailing full stop.
    /// </summary>
    [Fact]
    public async Task TestD1_TheSamePairIsFoundDespiteCaseAccentsAndSpacing()
    {
        SeedScenario(1, "Indisponibilidade do portal acadêmico", "Matrículas não realizadas.");

        var found = await Risks.FindScenarioDuplicatesAsync(new RiskScenarioDuplicateQuery
        {
            CentralEvent = "  INDISPONIBILIDADE do   portal academico ",
            Consequences = "matriculas nao realizadas"
        });

        var duplicate = Assert.Single(found);
        Assert.Equal(1, duplicate.RiskId);
        Assert.Equal("Legacy risk 1", duplicate.Subject);
        Assert.Equal("New", duplicate.Status);
    }

    /// <summary>
    /// D2 — the methodology's edge case: two scenarios with the same central event and different
    /// consequences are distinct, and neither is reported as the other's duplicate.
    /// </summary>
    [Fact]
    public async Task TestD2_TheSameEventWithDifferentConsequencesIsNotADuplicate()
    {
        SeedScenario(1, "Portal unavailable", "Enrolments lost");

        var found = await Risks.FindScenarioDuplicatesAsync(new RiskScenarioDuplicateQuery
        {
            CentralEvent = "Portal unavailable",
            Consequences = "Reputational damage with applicants"
        });

        Assert.Empty(found);
    }

    /// <summary>
    /// D3 — a warning, not a block: a second risk with exactly the same pair is created, and both are
    /// then each other's duplicate. Deliberately registering it is the user's call.
    /// </summary>
    [Fact]
    public async Task TestD3_ADuplicateDoesNotBlockCreation()
    {
        var first = await Risks.CreateRiskAsync(NewRisk("First", "Portal unavailable", "Enrolments lost"));
        var second = await Risks.CreateRiskAsync(NewRisk("Second", "Portal unavailable", "Enrolments lost"));

        Assert.NotNull(second);
        Assert.NotEqual(first!.Id, second!.Id);

        var ofSecond = await Risks.FindScenarioDuplicatesAsync(new RiskScenarioDuplicateQuery
        {
            CentralEvent = "Portal unavailable", Consequences = "Enrolments lost", ExcludeRiskId = second.Id
        });
        Assert.Equal(first.Id, Assert.Single(ofSecond).RiskId);
    }

    /// <summary>D4 — the risk being edited is not its own duplicate.</summary>
    [Fact]
    public async Task TestD4_TheExcludedRiskIsNotReported()
    {
        SeedScenario(1, "Portal unavailable", "Enrolments lost");

        var found = await Risks.FindScenarioDuplicatesAsync(new RiskScenarioDuplicateQuery
        {
            CentralEvent = "Portal unavailable", Consequences = "Enrolments lost", ExcludeRiskId = 1
        });

        Assert.Empty(found);
    }

    /// <summary>
    /// D5 — legacy risks never match: two risks with NULL fields are not each other's duplicate, and a
    /// risk with only an event matches nothing.
    /// </summary>
    [Fact]
    public async Task TestD5_RisksWithoutBothHalvesNeverMatch()
    {
        SeedScenario(1, null, null);
        SeedScenario(2, "Portal unavailable", null);
        SeedScenario(3, "Portal unavailable", "Enrolments lost");

        var found = await Risks.FindScenarioDuplicatesAsync(new RiskScenarioDuplicateQuery
        {
            CentralEvent = "Portal unavailable", Consequences = "Enrolments lost"
        });

        Assert.Equal(3, Assert.Single(found).RiskId);
    }

    /// <summary>D6 — half a pair is refused, naming the missing half, rather than answered with "no duplicate".</summary>
    [Theory]
    [InlineData(null, "Enrolments lost", "CentralEvent")]
    [InlineData("  ", "Enrolments lost", "CentralEvent")]
    [InlineData("Portal unavailable", null, "Consequences")]
    [InlineData("Portal unavailable", "", "Consequences")]
    public async Task TestD6_HalfAPairIsRefused(string? centralEvent, string? consequences, string parameter)
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Risks.FindScenarioDuplicatesAsync(new RiskScenarioDuplicateQuery
            {
                CentralEvent = centralEvent, Consequences = consequences
            }));

        Assert.Equal(parameter, ex.ParameterName);
    }

    /// <summary>D7 — a closed risk with the same scenario is reported, with its status, in id order.</summary>
    [Fact]
    public async Task TestD7_ClosedRisksAreReportedWithTheirStatus()
    {
        SeedScenario(2, "Portal unavailable", "Enrolments lost", status: "Closed");
        SeedScenario(1, "Portal unavailable", "Enrolments lost");

        var found = await Risks.FindScenarioDuplicatesAsync(new RiskScenarioDuplicateQuery
        {
            CentralEvent = "Portal unavailable", Consequences = "Enrolments lost"
        });

        Assert.Equal(new[] { 1, 2 }, found.Select(d => d.RiskId).ToArray());
        Assert.Equal("Closed", found[1].Status);
    }

    /// <summary>
    /// D8 — the caller's entity scope applies: a scoped user is not warned about — and so cannot learn
    /// the subject of — a matching risk outside their scope.
    /// </summary>
    [Fact]
    public async Task TestD8_TheCheckRespectsTheCallersEntityScope()
    {
        SeedScenario(1, "Portal unavailable", "Enrolments lost", entityId: UnitA);
        SeedScenario(2, "Portal unavailable", "Enrolments lost", entityId: UnitB);

        ScopeTo(UnitA);

        var found = await Risks.FindScenarioDuplicatesAsync(new RiskScenarioDuplicateQuery
        {
            CentralEvent = "Portal unavailable", Consequences = "Enrolments lost"
        });

        Assert.Equal(1, Assert.Single(found).RiskId);
    }
}
