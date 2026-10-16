using System.Text;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Engine;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// The index's object collection and day composition on a real MariaDB (T244–T248): every query the collector and the
/// register reader issue must translate to SQL — the in-memory provider evaluates what MySQL would refuse — and give
/// the values the unit tests assert. Seeded through EF at the target schema version. Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class RiskIndexCollectionIntegrationTests(MariaDbContainerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc);
    private const int Unit = 9101, Process = 9102, App = 9103, Host = 9201;

    private static Entity NewEntity(int id, string definition, int? parent) => new()
    {
        Id = id, DefinitionName = definition, DefinitionVersion = "2.7", Status = "active", Parent = parent,
        Created = Now.AddYears(-1), Updated = Now.AddYears(-1), CreatedBy = 1, UpdatedBy = 1
    };

    private static EntitiesProperty Property(int id, int entity, string key, string value) =>
        new() { Id = id, Entity = entity, Type = key, Name = key, Value = value, OldValue = "" };

    private async Task SeedAsync()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);

        await using var db = fixture.NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0; SET SESSION sql_mode = '';");

        db.Entities.AddRange(NewEntity(Unit, "organizationUnit", null), NewEntity(Process, "businessProcess", null),
            NewEntity(App, "application", null));
        db.EntitiesProperties.AddRange(
            Property(99001, Unit, "criticality", "3"),
            Property(99002, Process, "organizationUnit", Unit.ToString()),
            Property(99003, Process, "applications", App.ToString()),
            Property(99004, Process, "criticality", "5"),
            Property(99005, App, "criticality", "5"),
            Property(99006, App, "internetFacing", "True"));

        db.Hosts.Add(new Host
        {
            Id = Host, HostName = "db-02", Source = "test", Status = 1, EntityId = Unit, Criticality = 4,
            RegistrationDate = Now.AddDays(-100)
        });
        var finding = 0;
        Vulnerability Finding(int? host, int? entity, string source, string? severity, float? cvss3, float? vpr, bool exploit,
            string? cve, string? rule) => new()
        {
            Id = 990000 + ++finding, HostId = host, EntityId = entity, ImportSource = source, Severity = severity,
            Cvss3BaseScore = cvss3, VprScore = vpr, ExploitAvaliable = exploit, Cves = cve, RuleId = rule,
            LifecycleStatus = FindingStatus.Active, FirstDetection = Now.AddDays(-30), LastDetection = Now.AddDays(-1),
            Title = $"finding {finding}", Status = 1, DetectionCount = 1
        };
        db.Vulnerabilities.AddRange(
            Finding(Host, null, "nessus", null, 7.5f, 6.7f, false, "CVE-2026-0002", null),
            Finding(Host, null, "nessus", null, 5.3f, null, true, "CVE-2026-0003", null),
            Finding(Host, null, "nessus", null, 5.3f, null, true, "CVE-2026-0004", null),
            Finding(null, App, "semgrep", "High", null, null, false, null, "sqli"),
            Finding(null, App, "semgrep", "Medium", null, null, false, null, "xss-1"),
            Finding(null, App, "semgrep", "Medium", null, null, false, null, "xss-2"),
            Finding(null, App, "semgrep", "Medium", null, null, false, null, "xss-3"));
        db.ScanImports.Add(new ScanImport { Importer = "semgrep", EntityId = App, StartedAt = Now.AddDays(-2), FinishedAt = Now.AddDays(-2) });

        db.Incidents.Add(new Incident
        {
            Name = "2026-1", Description = "intrusion", Category = "unauthorized_access", ImpactedEntityId = App, EntityId = Unit,
            Status = (int)Model.IntStatus.New, CreationDate = Now.AddDays(-5), LastUpdate = Now.AddDays(-5), CreatedById = 1
        });

        db.RiskAppetites.Add(new RiskAppetite { EntityId = null, MaxAcceptableResidual = 5.0, CreatedAt = Now.AddYears(-1) });
        var risk = new Risk
        {
            Id = 9301, EntityId = Unit, Status = "New", Subject = "Enrolment leak", ReferenceId = "R-9301", Assessment = "",
            Notes = "", RiskCatalogMapping = "", ThreatCatalogMapping = "", SubmissionDate = new DateTime(2026, 1, 10),
            LastUpdate = new DateTime(2026, 1, 10), Owner = 98, SubmittedBy = 98
        };
        risk.Entities.Add(db.Entities.Local.Single(e => e.Id == Process));
        db.Risks.Add(risk);
        db.RiskScorings.Add(new RiskScoring { Id = 9301, CalculatedRisk = 8.8f, ResidualRisk = 8.4f, ClassicImpact = 5, ScoringMethod = 1 });
        await db.SaveChangesAsync();

        db.MgmtReviews.Add(new MgmtReview
        {
            RiskId = 9301, SubmissionDate = new DateTime(2026, 8, 21), Reviewer = 99, Comments = "", Review = 1, NextStep = 1,
            NextReview = new DateOnly(2026, 11, 19)
        });
        await db.SaveChangesAsync();

        db.RiskAcceptances.Add(new RiskAcceptance
        {
            Name = "R1", RiskId = 9301, StartDate = new DateTime(2026, 3, 20), CreatedAt = new DateTime(2026, 3, 20),
            ExpiresAt = new DateTime(2026, 9, 20), Status = RiskAcceptanceStatus.Expired, AuthorizingManagerId = 50, CreatedById = 50
        });
        db.Assessments.Add(new Assessment { Id = 9401, Name = "ISO", Created = Now.AddYears(-1) });
        db.AssessmentQuestions.Add(new AssessmentQuestion { Id = 9401, AssessmentId = 9401, Question = "Q", Order = 1 });
        db.AssessmentAnswers.AddRange(
            new AssessmentAnswer { Id = 9401, AssessmentId = 9401, QuestionId = 9401, Answer = "a", RiskSubject = Encoding.UTF8.GetBytes("s"), RiskScore = 3.5f, Order = 1 },
            new AssessmentAnswer { Id = 9402, AssessmentId = 9401, QuestionId = 9401, Answer = "b", RiskSubject = Encoding.UTF8.GetBytes("s"), RiskScore = 10f, Order = 2 });
        db.AssessmentRuns.Add(new AssessmentRun { Id = 9401, AssessmentId = 9401, EntityId = Unit, Status = 1, RunDate = Now.AddDays(-10) });
        db.AssessmentRunsAnswers.Add(new AssessmentRunsAnswer { Id = 9401, RunId = 9401, QuestionId = 9401, AnswerId = 9401 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task TestTheCollectorAndTheDayRunOnMariaDb()
    {
        await SeedAsync();
        var p = new RiskIndexParameters();

        await using var db = fixture.NewContext();
        var set = await RiskIndexObjectCollector.CollectAsync(db, p, RiskIndexMode.Interim, Now);

        RiskIndexObjectRow Row(RiskIndexObjectKind kind, int id, RiskIndexCategory category) =>
            set.Rows.Single(r => r.Key == new IcrObjectKey(kind, id.ToString()) && r.Category == category);

        Assert.Equal(0.641, Row(RiskIndexObjectKind.Host, Host, RiskIndexCategory.Exp).R, 3);
        Assert.Equal(0.928, Row(RiskIndexObjectKind.Application, App, RiskIndexCategory.Exp).R, 3);
        Assert.Equal(0.75, Row(RiskIndexObjectKind.TargetEntity, App, RiskIndexCategory.Ame).R, 6);
        Assert.Equal(0.196, Row(RiskIndexObjectKind.TargetEntity, Unit, RiskIndexCategory.Ctl).R, 6);

        var reg = Row(RiskIndexObjectKind.Scenario, 9301, RiskIndexCategory.Reg);
        Assert.Equal(new[] { Unit, Process }, reg.Attribution.OrderBy(i => i));
        Assert.Equal(new DateTime(2026, 8, 21), reg.ScenarioInputs!.ReferenceDate);
        Assert.InRange(reg.R, 0.8466, 0.8469);
        Assert.Contains(Row(RiskIndexObjectKind.Scenario, 9301, RiskIndexCategory.Gov).Signals, s => s.Id == "ACC-EXP");

        var day = RiskIndexDayCalculator.Compose(set, new RiskIndexDayRequest { Parameters = p, SnapshotInstant = Now });
        var unit = day.Nodes.Single(n => n.Node.EntityId == Unit);
        Assert.NotNull(unit.Result.Unfloored);
        Assert.Contains(new IcrObjectKey(RiskIndexObjectKind.Application, App.ToString()), unit.Members.Objects);
    }
}
