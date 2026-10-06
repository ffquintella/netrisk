using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Risks.Scenario;
using MySqlConnector;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.2 (S42 §5, §8 Q1–Q5) — the structured-scenario version on a real MariaDB: what the upgrade
/// does to rows that already exist (nothing but defaults), that a retry converges, that the relaxed
/// <c>pending_risks</c> columns and the new foreign key behave, that the EF model round-trips the
/// five risk columns, the hypothesis origin and the incident kind, and that the duplicate query keeps
/// its rules — and the caller's scope — on real SQL.
///
/// The version is found by its marker, never written here, and never read from <c>targetVersion</c>
/// for the "apply v − 1, seed, apply v" tests — that is how Stage 9.1's test broke when this version
/// landed on top of it (S42 §11, defect 1).
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9StructuredScenarioSchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`scenario_central_event`");

    private static int N => MariaDbContainerFixture.TargetSchemaVersion;

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static Task ExecAsync(MySqlConnection conn, string sql) => MariaDbContainerFixture.ExecAsync(conn, sql);

    private static async Task<object?> ScalarAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task ApplyVersionAsync(MySqlConnection conn, int version, params string[] parts)
    {
        foreach (var part in parts.Length == 0 ? ["Structure", "Data"] : parts)
            await ExecAsync(conn, await File.ReadAllTextAsync(
                Path.Combine(MariaDbContainerFixture.RepoDbDir(), part, $"{version}.sql")));
    }

    private static Task SeedUserAsync(MySqlConnection conn, int id) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`," +
        "`password`,`role_id`,`admin`,`login`) VALUES " +
        $"({id},1,0,'local','U{id}','u{id}@x.test','s',REPEAT('x',60),1,0,'user{id}');");

    private static Task SeedPreStageRowsAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `risks` (`id`,`status`,`subject`,`reference_id`,`assessment`,`notes`,`submission_date`," +
        "`last_update`,`risk_catalog_mapping`,`threat_catalog_mapping`,`template_group_id`,`submitted_by`) VALUES " +
        "(1,'New','Legacy risk','R-1','Free text','Notes','2025-01-01 00:00:00','2025-01-01 00:00:00','','',1,900);" +
        "INSERT INTO `pending_risks` (`id`,`assessment_id`,`assessment_answer_id`,`subject`,`score`,`comment`) " +
        "VALUES (1,3,4,'Shared credentials',6.5,'From the assessment');" +
        "INSERT INTO `incidents` (`Id`,`Year`,`Sequence`,`Name`,`Description`,`CreatedById`,`Status`) " +
        "VALUES (1,2026,1,'2026-1','Phishing',900,1);");

    /// <summary>
    /// Q1 — the upgrade adds the columns and changes no existing value: the legacy risk's five new
    /// columns are NULL (no back-fill, S42 §3), the pending row reads as an assessment row with no
    /// author, and the incident reads as an incident.
    /// </summary>
    [Fact]
    public async Task TestQ1_TheUpgradeLeavesExistingRowsAtTheirDefaults()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();
        await SeedUserAsync(conn, 900);
        await SeedPreStageRowsAsync(conn);

        await ApplyVersionAsync(conn, V);

        Assert.Equal(V.ToString(), await ScalarAsync(conn, "SELECT value FROM settings WHERE name = 'db_version'"));
        Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(conn,
            "SELECT COUNT(*) FROM risks WHERE scenario_cause IS NOT NULL OR scenario_vulnerability IS NOT NULL " +
            "OR scenario_central_event IS NOT NULL OR scenario_consequences IS NOT NULL OR evidence_confidence IS NOT NULL")));
        Assert.Equal((int)PendingRiskOrigin.Assessment, Convert.ToInt32(await ScalarAsync(conn,
            "SELECT origin FROM pending_risks WHERE id = 1")));
        Assert.Null(await ScalarAsync(conn, "SELECT submitted_by_id FROM pending_risks WHERE id = 1"));
        Assert.Equal((int)IncidentKind.Incident, Convert.ToInt32(await ScalarAsync(conn,
            "SELECT kind FROM incidents WHERE Id = 1")));
        Assert.Equal("Free text", await ScalarAsync(conn, "SELECT assessment FROM risks WHERE id = 1"));
    }

    /// <summary>
    /// Q2 — a retry converges: Structure applied again (with db_version back at v − 1, as after a
    /// failed Data script) and Data applied again leave one migration-history row and the same data.
    /// </summary>
    [Fact]
    public async Task TestQ2_ReplayingTheVersionConverges()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();
        await SeedUserAsync(conn, 900);
        await SeedPreStageRowsAsync(conn);

        await ApplyVersionAsync(conn, V);
        await ExecAsync(conn, $"UPDATE settings SET value = '{V - 1}' WHERE name = 'db_version';");
        await ApplyVersionAsync(conn, V, "Structure");
        await ApplyVersionAsync(conn, V, "Structure");
        await ApplyVersionAsync(conn, V, "Data");

        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9StructuredScenario'")));
        Assert.Equal(V.ToString(), await ScalarAsync(conn, "SELECT value FROM settings WHERE name = 'db_version'"));
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND TABLE_NAME = 'incidents' AND COLUMN_NAME = 'kind'")));
        Assert.Equal("YES", await ScalarAsync(conn,
            "SELECT IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND TABLE_NAME = 'pending_risks' AND COLUMN_NAME = 'assessment_id'"));
    }

    /// <summary>
    /// Q3 — a standalone hypothesis row (no assessment) is accepted; its author must exist; deleting
    /// the author keeps the hypothesis and clears the author (<c>ON DELETE SET NULL</c>).
    /// </summary>
    [Fact]
    public async Task TestQ3_AStandaloneRowIsAcceptedAndItsAuthorIsAForeignKey()
    {
        await fixture.InitializeNumberedSchemaAsync(N);
        await using var conn = await OpenAsync();
        await SeedUserAsync(conn, 901);

        await ExecAsync(conn,
            "INSERT INTO `pending_risks` (`id`,`assessment_id`,`assessment_answer_id`,`subject`,`score`,`comment`," +
            "`origin`,`submitted_by_id`) VALUES (2,NULL,NULL,'Hypothesis',0,'',2,901);");

        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn,
            "INSERT INTO `pending_risks` (`id`,`subject`,`score`,`comment`,`origin`,`submitted_by_id`) " +
            "VALUES (3,'Orphan',0,'',2,4040);"));

        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 901;");

        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(conn, "SELECT COUNT(*) FROM pending_risks WHERE id = 2")));
        Assert.Null(await ScalarAsync(conn, "SELECT submitted_by_id FROM pending_risks WHERE id = 2"));
    }

    /// <summary>
    /// Q4 — the EF model maps every new column onto the shipped DDL: a risk's four scenario fields and
    /// confidence, a standalone pending risk and a near miss written through a context read back as
    /// written, and the enums land as their integer values.
    /// </summary>
    [Fact]
    public async Task TestQ4_TheModelRoundTripsTheNewColumns()
    {
        await fixture.InitializeNumberedSchemaAsync(N);
        await using (var conn = await OpenAsync())
        {
            await SeedUserAsync(conn, 902);
        }

        await using (var write = fixture.NewContext())
        {
            write.Risks.Add(new Risk
            {
                Id = 10, Status = "New", Subject = "Payroll fraud", ReferenceId = "R-10", Assessment = "",
                Notes = "", RiskCatalogMapping = "", ThreatCatalogMapping = "", TemplateGroupId = 1,
                SubmissionDate = DateTime.UtcNow, LastUpdate = DateTime.UtcNow, SubmittedBy = 902,
                ScenarioCause = "An insider", ScenarioVulnerability = "No dual approval",
                ScenarioCentralEvent = "Salary redirected", ScenarioConsequences = "Financial loss",
                EvidenceConfidence = EvidenceConfidence.Indicative
            });
            write.PendingRisks.Add(new PendingRisk
            {
                Id = 10, Origin = PendingRiskOrigin.Standalone, SubmittedById = 902,
                Subject = Encoding.UTF8.GetBytes("Hypothesis"), Comment = "", SubmissionDate = DateTime.UtcNow
            });
            write.Incidents.Add(new Incident
            {
                Id = 10, Name = "2026-10", Description = "Phishing that was reported before anyone clicked",
                CreatedById = 902, Status = 1, Kind = IncidentKind.NearMiss
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.NewContext();
        var risk = await read.Risks.AsNoTracking().SingleAsync(r => r.Id == 10);
        Assert.Equal("An insider", risk.ScenarioCause);
        Assert.Equal("No dual approval", risk.ScenarioVulnerability);
        Assert.Equal("Salary redirected", risk.ScenarioCentralEvent);
        Assert.Equal("Financial loss", risk.ScenarioConsequences);
        Assert.Equal(EvidenceConfidence.Indicative, risk.EvidenceConfidence);

        var pending = await read.PendingRisks.AsNoTracking().SingleAsync(p => p.Id == 10);
        Assert.Equal(PendingRiskOrigin.Standalone, pending.Origin);
        Assert.Null(pending.AssessmentId);
        Assert.Equal(902, pending.SubmittedById);

        Assert.Equal(IncidentKind.NearMiss, (await read.Incidents.AsNoTracking().SingleAsync(i => i.Id == 10)).Kind);

        await using var conn2 = await OpenAsync();
        Assert.Equal(2, Convert.ToInt32(await ScalarAsync(conn2, "SELECT evidence_confidence FROM risks WHERE id = 10")));
        Assert.Equal(2, Convert.ToInt32(await ScalarAsync(conn2, "SELECT kind FROM incidents WHERE Id = 10")));
    }

    /// <summary>A DAL whose contexts carry a fixed entity scope, as a scoped caller's would.</summary>
    private sealed class ScopedDal(MariaDbContainerFixture f, EntityScope scope) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false)
        {
            var context = f.NewContext();
            context.EntityScope = scope;
            return context;
        }

        public EntityScope GetCurrentEntityScope() => scope;
    }

    private RisksService NewRisksService(EntityScope scope) => new(new ScopedDal(fixture, scope),
        Substitute.For<IRolesService>(), Substitute.For<ServerServices.Filtering.IEntityFilterMapperProvider>(),
        Substitute.For<IUsersService>(), Substitute.For<INotificationEventPublisher>(),
        Substitute.For<IRiskWorkflowService>());

    /// <summary>
    /// Q5 — the duplicate query translates to real SQL and keeps its rules there: the pair matches
    /// across case, accents and trailing punctuation, the excluded risk and the incomplete pair are left
    /// out, and a scoped caller is not told about the matching risk of another entity.
    /// </summary>
    [Fact]
    public async Task TestQ5_TheDuplicateQueryRunsOnRealSqlWithinTheCallersScope()
    {
        await fixture.InitializeNumberedSchemaAsync(N);
        await using (var conn = await OpenAsync())
        {
            await SeedUserAsync(conn, 903);
            await ExecAsync(conn,
                "INSERT INTO `entities` (`Id`,`DefinitionName`,`DefinitionVersion`,`CreatedBy`,`UpdatedBy`,`Status`) " +
                "VALUES (100,'organizationUnit','2.5',903,903,'active'),(200,'organizationUnit','2.5',903,903,'active');");

            static string Risk(int id, int unit, string? centralEvent, string? consequences) =>
                "INSERT INTO `risks` (`id`,`status`,`subject`,`reference_id`,`assessment`,`notes`,`submission_date`," +
                "`last_update`,`risk_catalog_mapping`,`threat_catalog_mapping`,`template_group_id`,`submitted_by`," +
                "`entity_id`,`scenario_central_event`,`scenario_consequences`) VALUES " +
                $"({id},'New','Risk {id}','R-{id}','','','2026-01-01 00:00:00','2026-01-01 00:00:00','','',1,903,{unit}," +
                $"{(centralEvent is null ? "NULL" : $"'{centralEvent}'")},{(consequences is null ? "NULL" : $"'{consequences}'")});";

            await ExecAsync(conn, Risk(1, 100, "Indisponibilidade do portal acadêmico", "Matrículas perdidas."));
            await ExecAsync(conn, Risk(2, 100, "indisponibilidade do portal academico", "matriculas perdidas"));
            await ExecAsync(conn, Risk(3, 100, "Indisponibilidade do portal acadêmico", null));
            await ExecAsync(conn, Risk(4, 200, "Indisponibilidade do portal acadêmico", "Matrículas perdidas"));
        }

        var query = new RiskScenarioDuplicateQuery
        {
            CentralEvent = "INDISPONIBILIDADE do portal acadêmico", Consequences = "Matrículas perdidas", ExcludeRiskId = 2
        };

        var unrestricted = await NewRisksService(EntityScope.Unrestricted).FindScenarioDuplicatesAsync(query);
        Assert.Equal(new[] { 1, 4 }, unrestricted.Select(d => d.RiskId).ToArray());

        var scoped = await NewRisksService(EntityScope.ForEntities([100])).FindScenarioDuplicatesAsync(query);
        Assert.Equal(1, Assert.Single(scoped).RiskId);
    }

    /// <summary>
    /// Q6 — the upgrade files every assessment-raised pending risk under its assessment's entity, so a
    /// scoped triager keeps the rows they could triage before; a row whose assessment has no entity
    /// stays organization-wide; a retry of the Data script changes nothing.
    /// </summary>
    [Fact]
    public async Task TestQ6_TheUpgradeFilesPendingRisksUnderTheirAssessmentsEntity()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();
        await SeedUserAsync(conn, 904);
        await ExecAsync(conn,
            "INSERT INTO `entities` (`Id`,`DefinitionName`,`DefinitionVersion`,`CreatedBy`,`UpdatedBy`,`Status`) " +
            "VALUES (100,'organizationUnit','2.5',904,904,'active');" +
            "INSERT INTO `assessments` (`id`,`name`,`entity_id`) VALUES (3,'Unit A assessment',100),(4,'Unscoped',NULL);" +
            "INSERT INTO `pending_risks` (`id`,`assessment_id`,`assessment_answer_id`,`subject`,`score`,`comment`) " +
            "VALUES (1,3,1,'In A',5,''),(2,4,2,'Nowhere',5,'');");

        await ApplyVersionAsync(conn, V);

        Assert.Equal(100, Convert.ToInt32(await ScalarAsync(conn, "SELECT entity_id FROM pending_risks WHERE id = 1")));
        Assert.Null(await ScalarAsync(conn, "SELECT entity_id FROM pending_risks WHERE id = 2"));

        await ExecAsync(conn, $"UPDATE settings SET value = '{V - 1}' WHERE name = 'db_version';");
        await ApplyVersionAsync(conn, V, "Structure");
        await ApplyVersionAsync(conn, V, "Data");
        Assert.Equal(100, Convert.ToInt32(await ScalarAsync(conn, "SELECT entity_id FROM pending_risks WHERE id = 1")));
    }

    /// <summary>
    /// Q7 — the pending-risk query filter is enforced by the database, not the client (the reason
    /// <c>EntityScopeQueryFilterTests</c> exists): a context scoped to A reads A's row only, the
    /// generated SQL carries the predicate, and deleting the entity leaves the row organization-wide.
    /// </summary>
    [Fact]
    public async Task TestQ7_ThePendingRiskScopeFilterRunsInSql()
    {
        await fixture.InitializeNumberedSchemaAsync(N);
        await using (var conn = await OpenAsync())
        {
            await SeedUserAsync(conn, 905);
            await ExecAsync(conn,
                "INSERT INTO `entities` (`Id`,`DefinitionName`,`DefinitionVersion`,`CreatedBy`,`UpdatedBy`,`Status`) " +
                "VALUES (100,'organizationUnit','2.5',905,905,'active'),(200,'organizationUnit','2.5',905,905,'active');" +
                "INSERT INTO `pending_risks` (`id`,`subject`,`score`,`comment`,`origin`,`entity_id`) " +
                "VALUES (1,'In A',0,'',2,100),(2,'In B',0,'',2,200),(3,'Global',0,'',2,NULL);");
        }

        await using (var scoped = fixture.NewScopedContext(100))
        {
            var query = scoped.PendingRisks.AsNoTracking().OrderBy(p => p.Id);
            Assert.Equal(new[] { 1 }, await query.Select(p => p.Id).ToArrayAsync());
            Assert.Contains("entity_id", query.ToQueryString());
        }

        await using (var unrestricted = fixture.NewContext())
            Assert.Equal(3, await unrestricted.PendingRisks.CountAsync());

        await using var conn2 = await OpenAsync();
        await ExecAsync(conn2, "DELETE FROM `entities` WHERE `Id` = 200;");
        Assert.Null(await ScalarAsync(conn2, "SELECT entity_id FROM pending_risks WHERE id = 2"));
    }
}
