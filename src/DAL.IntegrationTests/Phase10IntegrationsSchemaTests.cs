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
/// End-to-end Phase 10 (Track 4) against the real schema on MariaDB: builds the numbered schema
/// through 78, applies Phase 10 through the actual upgrade service and the shipped manifest and SQL,
/// and verifies every table, column, index and foreign key the integrations depend on.
///
/// This is the half of the migration ritual that actually reaches a production database — the EF
/// migration only keeps the model and snapshot honest — so it is worth proving the hand-written SQL
/// applies rather than assuming it matches what EF generated.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Phase10IntegrationsSchemaTests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) =>
            f.NewContext();

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private SchemaUpgradeService NewService(string backupDir)
    {
        Directory.CreateDirectory(backupDir);

        var db = Substitute.For<IDatabaseService>();
        db.Status().Returns(new DatabaseStatus { Status = "Online", Version = "78", ServerVersion = "10.11" });
        db.When(x => x.Backup(Arg.Any<string>()))
            .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p10.sql"), "-- dump"));

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

    private async Task<MySqlConnection> ApplyPhase10Async(string label)
    {
        await fixture.InitializeNumberedSchemaAsync(78);

        var service = NewService(Path.Combine(Path.GetTempPath(), $"nr-p10-{label}-" + Guid.NewGuid().ToString("N")));

        var check = service.Check("10", "homolog");
        Assert.True(check.Success,
            string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        var report = service.Apply("10", "homolog", yes: true);
        Assert.True(report.Success,
            string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        return conn;
    }

    [Fact]
    public async Task Phase10_CreatesEveryIntegrationTableAndReachesVersion79()
    {
        await using var conn = await ApplyPhase10Async("tables");

        Assert.Equal("15", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() " +
            "AND table_name IN ('notification_channels','notification_subscriptions'," +
            "'notification_deliveries','issue_tracker_connections','issue_status_mappings'," +
            "'finding_issue_links','identity_providers','scim_tokens','scim_request_logs'," +
            "'webauthn_credentials','mfa_recovery_codes','trendmicro_connections'," +
            "'securityscorecard_connections','security_scorecard_factors','integration_sync_logs');"));

        Assert.Equal("79", await ScalarAsync(conn, "SELECT value FROM settings WHERE name = 'db_version';"));
    }

    [Fact]
    public async Task Phase10_AddsThePostureColumnsToHostsAndEntities()
    {
        await using var conn = await ApplyPhase10Async("columns");

        Assert.Equal("7", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name = 'hosts' AND column_name IN ('external_id','external_provider'," +
            "'os_version','criticality','risk_score','risk_score_source','risk_score_updated_at');"));

        Assert.Equal("4", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name = 'entities' AND column_name IN ('cyber_risk_index','posture_grade'," +
            "'posture_source','posture_updated_at');"));

        // The index the inventory sync's per-device lookup rides on. Without it every device costs a
        // scan of the host table.
        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics " +
            "WHERE table_schema = DATABASE() AND table_name = 'hosts' " +
            "AND index_name = 'idx_hosts_external_provider_id';"));
    }

    [Fact]
    public async Task Phase10_TextColumnsAreVarcharOrTextAndNeverBlob()
    {
        await using var conn = await ApplyPhase10Async("types");

        // Track 6 convention: never BLOB for text. A BLOB column is invisible to a LIKE search and
        // comes back as bytes in every client.
        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name IN ('notification_channels','notification_deliveries'," +
            "'issue_tracker_connections','identity_providers','trendmicro_connections'," +
            "'securityscorecard_connections') AND data_type LIKE '%blob%';"));

        // And never char(n) for a string: EF Core 10 treats a char(n) string as a primitive collection
        // of char and the model build dies with a NullReferenceException that names no property.
        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name IN ('notification_channels','notification_subscriptions'," +
            "'notification_deliveries','issue_tracker_connections','issue_status_mappings'," +
            "'finding_issue_links','identity_providers','scim_tokens','scim_request_logs'," +
            "'webauthn_credentials','mfa_recovery_codes','trendmicro_connections'," +
            "'securityscorecard_connections','security_scorecard_factors','integration_sync_logs') " +
            "AND data_type = 'char';"));

        // Booleans are tinyint(1), enums are int.
        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "AND table_name = 'notification_channels' " +
            "AND column_name IN ('enabled','secrets_encrypted') AND column_type <> 'tinyint(1)';"));
    }

    [Fact]
    public async Task Phase10_TheUniquenessGuardsAreEnforcedByTheDatabase()
    {
        await using var conn = await ApplyPhase10Async("unique");

        Assert.Equal("9", await ScalarAsync(conn,
            "SELECT COUNT(DISTINCT CONCAT(table_name,'.',index_name)) FROM information_schema.statistics " +
            "WHERE table_schema = DATABASE() AND non_unique = 0 AND index_name IN (" +
            "'uq_notification_channels_name','uq_issue_tracker_connections_name'," +
            "'uq_issue_status_mappings_connection_status','uq_finding_issue_links_connection_issue'," +
            "'uq_identity_providers_name','uq_scim_tokens_key_id'," +
            "'uq_webauthn_credentials_credential_id','uq_trendmicro_connections_name'," +
            "'uq_securityscorecard_connections_name');"));
    }

    [Fact]
    public async Task Phase10_TheSameIssueCannotBeLinkedTwiceOnOneConnection()
    {
        await using var conn = await ApplyPhase10Async("dup");

        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");
        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 0;");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `finding_issue_links` (`vulnerability_id`,`connection_id`,`issue_key`," +
            "`last_change_from_remote`,`has_conflict`,`created_at`) VALUES (1,1,'SEC-1',0,0,NOW());");

        // Re-running "create issue" for the same finding must not produce two links to the same ticket,
        // and the inbound webhook looks a link up by exactly this pair.
        await Assert.ThrowsAsync<MySqlException>(async () =>
            await MariaDbContainerFixture.ExecAsync(conn,
                "INSERT INTO `finding_issue_links` (`vulnerability_id`,`connection_id`,`issue_key`," +
                "`last_change_from_remote`,`has_conflict`,`created_at`) VALUES (2,1,'SEC-1',0,0,NOW());"));
    }

    [Fact]
    public async Task Phase10_DeletingAConnectionTakesItsLinksAndMappingsWithIt()
    {
        await using var conn = await ApplyPhase10Async("cascade");

        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");
        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 0;");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `vulnerabilities` (`Id`,`Title`) VALUES (1,'finding');");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `issue_tracker_connections` (`id`,`name`,`provider`,`base_url`,`project_key`," +
            "`enabled`,`push_finding_updates`,`poll_interval_minutes`,`created_at`) " +
            "VALUES (1,'jira',1,'https://x','SEC',1,1,15,NOW());");

        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 1;");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `finding_issue_links` (`vulnerability_id`,`connection_id`,`issue_key`," +
            "`last_change_from_remote`,`has_conflict`,`created_at`) VALUES (1,1,'SEC-1',0,0,NOW());");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `issue_status_mappings` (`connection_id`,`external_status`,`action`) " +
            "VALUES (1,'Done',1);");

        await MariaDbContainerFixture.ExecAsync(conn,
            "DELETE FROM `issue_tracker_connections` WHERE `id` = 1;");

        // A link or a mapping without its connection cannot do anything, so neither is a row worth
        // keeping.
        Assert.Equal("0", await ScalarAsync(conn, "SELECT COUNT(*) FROM `finding_issue_links`;"));
        Assert.Equal("0", await ScalarAsync(conn, "SELECT COUNT(*) FROM `issue_status_mappings`;"));
    }

    [Fact]
    public async Task Phase10_DeletingASubscriptionKeepsItsDeliveryLog()
    {
        await using var conn = await ApplyPhase10Async("log");

        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `notification_channels` (`id`,`name`,`kind`,`configuration_json`," +
            "`secrets_encrypted`,`enabled`,`created_at`) VALUES (1,'slack',2,'{}',1,1,NOW());");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `notification_subscriptions` (`id`,`event_type`,`channel_id`,`enabled`," +
            "`created_at`) VALUES (1,1,1,1,NOW());");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `notification_deliveries` (`subscription_id`,`channel_id`,`event_type`," +
            "`status`,`attempts`,`created_at`) VALUES (1,1,1,5,3,NOW());");

        await MariaDbContainerFixture.ExecAsync(conn,
            "DELETE FROM `notification_subscriptions` WHERE `id` = 1;");

        // Deleting a misconfigured subscription must not erase the evidence that it failed to deliver
        // anything for a month.
        Assert.Equal("1", await ScalarAsync(conn, "SELECT COUNT(*) FROM `notification_deliveries`;"));
        Assert.Equal("", await ScalarAsync(conn,
            "SELECT IFNULL(subscription_id,'') FROM `notification_deliveries` LIMIT 1;"));
    }

    [Fact]
    public async Task Phase10_RevokingAScimTokenKeepsItsRequestAudit()
    {
        await using var conn = await ApplyPhase10Async("scim");

        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `scim_tokens` (`id`,`name`,`key_id`,`secret_hash`,`created_at`) " +
            "VALUES (1,'entra','abcd','hash',NOW());");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `scim_request_logs` (`token_id`,`method`,`path`,`status_code`,`occurred_at`) " +
            "VALUES (1,'PATCH','/scim/v2/Users/1',200,NOW());");

        await MariaDbContainerFixture.ExecAsync(conn, "DELETE FROM `scim_tokens` WHERE `id` = 1;");

        // Deleting a provisioning token must not delete the record of what it did.
        Assert.Equal("1", await ScalarAsync(conn, "SELECT COUNT(*) FROM `scim_request_logs`;"));
    }

    [Fact]
    public async Task Phase10_IsSafeToApplyTwice()
    {
        await fixture.InitializeNumberedSchemaAsync(78);

        var service = NewService(Path.Combine(Path.GetTempPath(), "nr-p10-retry-" + Guid.NewGuid().ToString("N")));

        Assert.True(service.Apply("10", "homolog", yes: true).Success);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        // Re-running the Structure script is what a retry after a part-applied upgrade does, and every
        // statement in it is guarded so it converges rather than failing on the first CREATE TABLE.
        var structure = await File.ReadAllTextAsync(
            Path.Combine(MariaDbContainerFixture.RepoDbDir(), "Structure", "79.sql"));

        await MariaDbContainerFixture.ExecAsync(conn, structure);

        Assert.Equal("15", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() " +
            "AND table_name IN ('notification_channels','notification_subscriptions'," +
            "'notification_deliveries','issue_tracker_connections','issue_status_mappings'," +
            "'finding_issue_links','identity_providers','scim_tokens','scim_request_logs'," +
            "'webauthn_credentials','mfa_recovery_codes','trendmicro_connections'," +
            "'securityscorecard_connections','security_scorecard_factors','integration_sync_logs');"));
    }
}
