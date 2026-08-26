using System;
using System.IO;
using System.Threading.Tasks;
using DAL.Context;
using Microsoft.Extensions.Configuration;
using Model.Database;
using MySqlConnector;
using NSubstitute;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.SchemaUpgrade;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Track 8's three upgrade phases against a real MariaDB: the governance core (11 → db_version 80),
/// the business review portal (12 → 81) and the schema the deferred Track 7 findings needed
/// (13 → 82).
///
/// This is the half of the migration ritual that actually reaches a production database — the EF
/// migration only keeps the model and its snapshot honest — so the hand-written SQL is proved to
/// apply rather than assumed to match what EF generated. Two of Track 8's own defects were only
/// visible here: a permission id that collided with an existing row, and a floor default that a
/// backfill had to clear.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track8GovernanceSchemaTests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) =>
            f.NewContext();

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private SchemaUpgradeService NewService(string backupDir, int fromVersion)
    {
        Directory.CreateDirectory(backupDir);

        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus
        {
            Status = "Online", Version = fromVersion.ToString(), ServerVersion = "10.11"
        });
        db.When(x => x.Backup(Arg.Any<string>()))
            .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_t8.sql"), "-- dump"));

        return new SchemaUpgradeService(db, new ContainerDal(fixture), Substitute.For<IConfiguration>(),
            Substitute.For<ILogger>())
        {
            DbDirectory = MariaDbContainerFixture.RepoDbDir(),
            BackupDirectory = backupDir,
            ConnectionString = fixture.ConnectionString,
            Operator = "itest"
        };
    }

    private static async Task<string> ScalarAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? "";
    }

    /// <summary>Builds the schema up to <paramref name="upTo"/> and applies the named phases in order.</summary>
    private async Task<MySqlConnection> ApplyAsync(string label, int upTo, params string[] phases)
    {
        await fixture.InitializeNumberedSchemaAsync(upTo);

        var from = upTo;

        foreach (var phase in phases)
        {
            var service = NewService(
                Path.Combine(Path.GetTempPath(), $"nr-t8-{label}-{phase}-" + Guid.NewGuid().ToString("N")),
                from);

            var check = service.Check(phase, "homolog");
            Assert.True(check.Success,
                string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

            var report = service.Apply(phase, "homolog", yes: true);
            Assert.True(report.Success,
                string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

            from++;
        }

        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        return conn;
    }

    // --- phase 11: the governance core ----------------------------------------------------------

    [Fact]
    public async Task Phase11_CreatesTheGovernanceTablesAndReachesVersion80()
    {
        await using var conn = await ApplyAsync("core", 79, "11");

        Assert.Equal("3", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() " +
            "AND table_name IN ('risk_appetites','audit_logs','mitigation_tasks');"));

        Assert.Equal("80", await ScalarAsync(conn, "SELECT value FROM settings WHERE name = 'db_version';"));
    }

    [Fact]
    public async Task Phase11_AddsResidualAndQuantitativeScoring()
    {
        await using var conn = await ApplyAsync("scoring", 79, "11");

        Assert.Equal("2", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name = 'risk_scoring' AND column_name IN ('residual_risk','residual_updated_at');"));

        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name = 'risk_scoring_history' AND column_name = 'residual_risk';"));

        Assert.Equal("16", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            @"AND table_name = 'risk_scoring' AND column_name LIKE 'quant\_%';"));
    }

    /// <summary>
    /// <c>start_date</c> is NOT NULL, so it arrives with a floor default and the Data script has to
    /// clear it. A row left at the floor is an acceptance that claims to have started in the year
    /// 1000 — which is the kind of thing nobody notices until an auditor sorts by it.
    /// </summary>
    [Fact]
    public async Task Phase11_BackfillsTheAcceptanceStartDateRatherThanLeavingTheFloor()
    {
        await fixture.InitializeNumberedSchemaAsync(79);

        await using (var seed = new MySqlConnection(fixture.ConnectionString))
        {
            await seed.OpenAsync();

            await MariaDbContainerFixture.ExecAsync(seed,
                "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`," +
                "`password`,`role_id`,`admin`,`login`) VALUES (900,1,0,'local','T8','t8@x.test','s'," +
                "REPEAT('x',60),1,1,'t8user');");

            await MariaDbContainerFixture.ExecAsync(seed,
                "INSERT INTO `risk_acceptances` (`id`,`name`,`authorizing_manager_id`,`expires_at`," +
                "`status_id`,`created_at`) VALUES (900,'Legacy finding exception',900," +
                "'2026-12-31 00:00:00',1,'2026-03-01 09:30:00');");
        }

        var service = NewService(
            Path.Combine(Path.GetTempPath(), "nr-t8-backfill-" + Guid.NewGuid().ToString("N")), 79);

        Assert.True(service.Apply("11", "homolog", yes: true).Success);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM risk_acceptances WHERE start_date <= '1000-01-02 00:00:00';"));

        // Backfilled from created_at, which is the only date the row actually carries.
        Assert.Equal("2026-03-01 09:30:00", await ScalarAsync(conn,
            "SELECT DATE_FORMAT(start_date, '%Y-%m-%d %H:%i:%s') FROM risk_acceptances WHERE id = 900;"));
    }

    [Fact]
    public async Task Phase11_ReCreatesTheCadenceSettingDeletedInVersion29()
    {
        await using var conn = await ApplyAsync("cadence", 79, "11");

        // Seeded in version 1 and deleted in version 29 along with `risk_appetite`, so the spec's
        // claim that the setting "hints at the concept" described a row that had not existed for
        // fifty versions.
        Assert.Equal("InherentRisk", await ScalarAsync(conn,
            "SELECT value FROM settings WHERE name = 'next_review_date_uses';"));
    }

    [Fact]
    public async Task Phase11_GivesEveryScaleLevelAWrittenDefinition()
    {
        await using var conn = await ApplyAsync("anchors", 79, "11");

        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT (SELECT COUNT(*) FROM likelihood WHERE definition IS NULL) + " +
            "(SELECT COUNT(*) FROM impact WHERE definition IS NULL);"));

        Assert.Equal("5", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM likelihood WHERE probability_min IS NOT NULL " +
            "AND probability_max IS NOT NULL;"));
    }

    [Fact]
    public async Task Phase11_TextColumnsAreVarcharOrTextAndNeverBlobOrChar()
    {
        await using var conn = await ApplyAsync("types", 79, "11");

        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name IN ('risk_appetites','audit_logs','mitigation_tasks') " +
            "AND data_type LIKE '%blob%';"));

        // A char(n) string makes EF Core 10 treat it as a primitive collection of char, and the model
        // build dies with a NullReferenceException naming no property.
        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name IN ('risk_appetites','audit_logs','mitigation_tasks') " +
            "AND data_type = 'char';"));
    }

    // --- phase 12: the portal ---------------------------------------------------------------------

    [Fact]
    public async Task Phase12_CreatesThePortalTablesAndReachesVersion81()
    {
        await using var conn = await ApplyAsync("portal", 79, "11", "12");

        Assert.Equal("3", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() " +
            "AND table_name IN ('entity_risk_reviewers','risk_review_campaigns'," +
            "'risk_review_campaign_items');"));

        Assert.Equal("81", await ScalarAsync(conn, "SELECT value FROM settings WHERE name = 'db_version';"));
    }

    /// <summary>
    /// The unique (entity, period) index is what makes the daily campaign generator idempotent. Without
    /// it the job creates a new campaign every morning and a reviewer's list fills with duplicates.
    /// </summary>
    [Fact]
    public async Task Phase12_CampaignGenerationIsIdempotentByConstruction()
    {
        await using var conn = await ApplyAsync("idempotent", 79, "11", "12");

        Assert.Equal("3", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() " +
            "AND table_name = 'risk_review_campaigns' " +
            "AND index_name = 'uq_risk_review_campaigns_entity_period' AND non_unique = 0;"));
    }

    /// <summary>
    /// The regression that made this test worth writing: the first draft of Data/81.sql named
    /// permission id 50 with an <c>ON DUPLICATE KEY UPDATE</c> clause. Id 50 is
    /// <c>incident-response-plans</c>, allocated by auto_increment in Data/34.sql, so the upsert
    /// renamed that permission and never created the new one — on every upgraded installation, with
    /// nothing in the schema to show for it.
    /// </summary>
    [Fact]
    public async Task Phase12_AddsTheBusinessReviewPermissionWithoutDisturbingAnyOther()
    {
        await using var conn = await ApplyAsync("permission", 79, "11", "12");

        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM permissions WHERE `key` = 'business_risk_review';"));

        Assert.Equal("Incident Response Plans", await ScalarAsync(conn,
            "SELECT name FROM permissions WHERE `key` = 'incident-response-plans';"));

        // Every key is still unique — an upsert on a colliding id would have left one fewer row.
        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM (SELECT `key` FROM permissions GROUP BY `key` HAVING COUNT(*) > 1) d;"));
    }

    // --- phase 13: the deferred security schema ---------------------------------------------------

    [Fact]
    public async Task Phase13_CreatesTheSecurityTablesAndReachesVersion82()
    {
        await using var conn = await ApplyAsync("security", 79, "11", "12", "13");

        Assert.Equal("2", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() " +
            "AND table_name IN ('revoked_tokens','login_attempts');"));

        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name = 'nr_files' AND column_name = 'entity_id';"));

        Assert.Equal("82", await ScalarAsync(conn, "SELECT value FROM settings WHERE name = 'db_version';"));
    }

    /// <summary>
    /// The attachment backfill (NR-2026-017). A file whose parent risk carries an entity inherits it;
    /// one whose parent does not keeps a NULL and stays visible, which is the honest outcome rather
    /// than a guess.
    /// </summary>
    [Fact]
    public async Task Phase13_BackfillsAttachmentEntitiesFromTheirParentRisk()
    {
        await fixture.InitializeNumberedSchemaAsync(79);

        var service11 = NewService(Path.Combine(Path.GetTempPath(), "nr-t8-b11-" + Guid.NewGuid().ToString("N")), 79);
        Assert.True(service11.Apply("11", "homolog", yes: true).Success);

        var service12 = NewService(Path.Combine(Path.GetTempPath(), "nr-t8-b12-" + Guid.NewGuid().ToString("N")), 80);
        Assert.True(service12.Apply("12", "homolog", yes: true).Success);

        await using (var seed = new MySqlConnection(fixture.ConnectionString))
        {
            await seed.OpenAsync();

            await MariaDbContainerFixture.ExecAsync(seed,
                "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`," +
                "`password`,`role_id`,`admin`,`login`) VALUES (901,1,0,'local','T8b','t8b@x.test'," +
                "'s',REPEAT('x',60),1,1,'t8buser');");

            await MariaDbContainerFixture.ExecAsync(seed,
                "INSERT INTO `entities` (`Id`,`DefinitionName`,`DefinitionVersion`,`Created`," +
                "`Updated`,`CreatedBy`,`UpdatedBy`,`Status`) VALUES (901,'organization','1',NOW()," +
                "NOW(),901,901,'active');");

            await MariaDbContainerFixture.ExecAsync(seed,
                "INSERT INTO `risks` (`id`,`status`,`subject`,`reference_id`,`assessment`,`notes`," +
                "`submission_date`,`last_update`,`risk_catalog_mapping`,`threat_catalog_mapping`," +
                "`template_group_id`,`entity_id`,`submitted_by`) VALUES (901,'New','Scoped risk'," +
                "'R-901','','',NOW(),NOW(),'','',1,901,901);");

            await MariaDbContainerFixture.ExecAsync(seed,
                "INSERT INTO `risks` (`id`,`status`,`subject`,`reference_id`,`assessment`,`notes`," +
                "`submission_date`,`last_update`,`risk_catalog_mapping`,`threat_catalog_mapping`," +
                "`template_group_id`,`submitted_by`) VALUES (902,'New','Unscoped risk','R-902','',''," +
                "NOW(),NOW(),'','',1,901);");

            await MariaDbContainerFixture.ExecAsync(seed,
                "INSERT INTO `nr_files` (`id`,`risk_id`,`name`,`unique_name`,`size`,`timestamp`," +
                "`user`,`content`) VALUES (901,901,'scoped.pdf','u-901',3,NOW(),901,'abc');");

            await MariaDbContainerFixture.ExecAsync(seed,
                "INSERT INTO `nr_files` (`id`,`risk_id`,`name`,`unique_name`,`size`,`timestamp`," +
                "`user`,`content`) VALUES (902,902,'unscoped.pdf','u-902',3,NOW(),901,'abc');");
        }

        var service13 = NewService(Path.Combine(Path.GetTempPath(), "nr-t8-b13-" + Guid.NewGuid().ToString("N")), 81);
        Assert.True(service13.Apply("13", "homolog", yes: true).Success);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        Assert.Equal("901", await ScalarAsync(conn, "SELECT entity_id FROM nr_files WHERE id = 901;"));
        Assert.Equal("", await ScalarAsync(conn, "SELECT entity_id FROM nr_files WHERE id = 902;"));
    }

    [Fact]
    public async Task Phase13_TheRevocationListAndLockoutCounterAreUniquelyKeyed()
    {
        await using var conn = await ApplyAsync("keys", 79, "11", "12", "13");

        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() " +
            "AND table_name = 'revoked_tokens' AND index_name = 'uq_revoked_tokens_jti' " +
            "AND non_unique = 0;"));

        Assert.Equal("2", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() " +
            "AND table_name = 'login_attempts' " +
            "AND index_name = 'uq_login_attempts_identity_source' AND non_unique = 0;"));
    }
}
