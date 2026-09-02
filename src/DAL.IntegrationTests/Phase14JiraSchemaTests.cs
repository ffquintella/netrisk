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
/// End-to-end Phase 14 (Track 4 milestone 4.6) against the real schema on MariaDB: builds the
/// numbered schema through 82, applies Phase 14 through the actual upgrade service and the shipped
/// manifest and SQL, and verifies the tables, columns, indexes and constraints the Jira integration
/// depends on.
///
/// The two things only a real database can prove are here. The <c>CHECK</c> constraint that holds the
/// "exactly one target" invariant on <c>finding_issue_links</c> — which the in-memory provider ignores
/// entirely, so the unit tests cover the code guard and this covers the backstop. And that the
/// widening is genuinely additive: an issue link written before 4.6 still reads as a finding link
/// afterwards, with no backfill.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Phase14JiraSchemaTests(MariaDbContainerFixture fixture)
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
        db.Status().Returns(new DatabaseStatus
        {
            Status = "Online", Version = "82", ServerVersion = "10.11"
        });
        db.When(x => x.Backup(Arg.Any<string>()))
            .Do(ci => File.WriteAllText(Path.Combine(ci.Arg<string>(), "backup_p14.sql"), "-- dump"));

        return new SchemaUpgradeService(db, new ContainerDal(fixture),
            Substitute.For<IConfiguration>(), Substitute.For<ILogger>())
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

    private static async Task ExecuteAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<MySqlConnection> ApplyPhase14Async(string label,
        Func<MySqlConnection, Task>? beforeUpgrade = null)
    {
        await fixture.InitializeNumberedSchemaAsync(82);

        if (beforeUpgrade != null)
        {
            await using var seed = new MySqlConnection(fixture.ConnectionString);
            await seed.OpenAsync();
            await beforeUpgrade(seed);
        }

        var service = NewService(Path.Combine(Path.GetTempPath(),
            $"nr-p14-{label}-" + Guid.NewGuid().ToString("N")));

        var check = service.Check("14", "homolog");
        Assert.True(check.Success,
            string.Join("; ", check.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        var report = service.Apply("14", "homolog", yes: true);
        Assert.True(report.Success,
            string.Join("; ", report.Checks.ConvertAll(c => $"{c.Name}={c.Passed}:{c.Detail}")));

        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        return conn;
    }

    [Fact]
    public async Task Phase14_CreatesEveryJiraTableAndReachesVersion83()
    {
        await using var conn = await ApplyPhase14Async("tables");

        Assert.Equal("8", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() "
            + "AND table_name IN ('jira_connection_settings', 'jira_queue_imports', "
            + "'jira_service_requests', 'jira_request_slas', 'jira_field_mappings', "
            + "'jira_object_mappings', 'jira_object_attribute_mappings', 'jira_asset_objects')"));

        Assert.Equal("83", await ScalarAsync(conn,
            "SELECT value FROM settings WHERE name = 'db_version'"));
    }

    [Fact]
    public async Task Phase14_AddsTheTwoCmdbColumnsToHosts()
    {
        await using var conn = await ApplyPhase14Async("hosts");

        Assert.Equal("2", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() "
            + "AND table_name = 'hosts' AND column_name IN ('environment', 'owner')"));

        // The active state deliberately has no column of its own: hosts.status already holds an
        // IntStatus, and a parallel boolean would give one fact two homes that can disagree.
        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() "
            + "AND table_name = 'hosts' AND column_name = 'active'"));
    }

    [Fact]
    public async Task Phase14_WidensIssueLinksToIncidentsAndRisks()
    {
        await using var conn = await ApplyPhase14Async("links");

        Assert.Equal("3", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() "
            + "AND table_name = 'finding_issue_links' "
            + "AND column_name IN ('target_kind', 'incident_id', 'risk_id')"));

        // Nullable, so a link can name an incident or a risk instead.
        Assert.Equal("YES", await ScalarAsync(conn,
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema = DATABASE() "
            + "AND table_name = 'finding_issue_links' AND column_name = 'vulnerability_id'"));

        // Real foreign keys, not a polymorphic id: a polymorphic id cannot cascade, so deleting a
        // risk would leave a link pointing at nothing.
        Assert.Equal("2", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.referential_constraints "
            + "WHERE constraint_schema = DATABASE() "
            + "AND constraint_name IN ('fk_finding_issue_links_incident_id', "
            + "'fk_finding_issue_links_risk_id') AND delete_rule = 'CASCADE'"));
    }

    /// <summary>
    /// The widening is additive: a link written before 4.6 still reads as a finding link afterwards.
    ///
    /// This is the property that makes the upgrade safe to run on a live database. The column default
    /// carries it, so there is no backfill to get wrong — but the default is only load-bearing if the
    /// <c>ALTER</c> really does apply it to existing rows, which is what this asserts.
    /// </summary>
    [Fact]
    public async Task Phase14_LeavesAPreExistingLinkReadableAsAFindingLink()
    {
        await using var conn = await ApplyPhase14Async("existing", async seed =>
        {
            await ExecuteAsync(seed,
                "INSERT INTO issue_tracker_connections (id, name, provider, base_url, project_key, "
                + "enabled, push_finding_updates, poll_interval_minutes, created_at) "
                + "VALUES (1, 'Legacy Jira', 1, 'https://acme.atlassian.net', 'SEC', 1, 1, 15, NOW())");

            await ExecuteAsync(seed,
                "INSERT INTO hosts (Id, HostName, Source, RegistrationDate, Status) "
                + "VALUES (1, 'srv-01', 'manual', NOW(), 1)");

            await ExecuteAsync(seed,
                "INSERT INTO vulnerabilities (Id, Title, Severity, FirstDetection, LastDetection, "
                + "Status, HostId, DetectionCount) "
                + "VALUES (42, 'SQL injection', 'critical', NOW(), NOW(), 1, 1, 1)");

            await ExecuteAsync(seed,
                "INSERT INTO finding_issue_links (id, vulnerability_id, connection_id, issue_key, "
                + "last_change_from_remote, has_conflict, created_at) "
                + "VALUES (1, 42, 1, 'SEC-1', 0, 0, NOW())");
        });

        // 1 is IssueLinkTargetKind.Finding.
        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT target_kind FROM finding_issue_links WHERE id = 1"));

        Assert.Equal("42", await ScalarAsync(conn,
            "SELECT vulnerability_id FROM finding_issue_links WHERE id = 1"));

        Assert.Equal("0", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM finding_issue_links WHERE incident_id IS NOT NULL "
            + "OR risk_id IS NOT NULL"));
    }

    /// <summary>
    /// The database refuses a link with no target and a link with two.
    ///
    /// The in-memory provider ignores <c>CHECK</c> constraints entirely, so the unit tests can only
    /// cover <c>FindingIssueLink.Validate()</c>. This is the backstop for a code path that forgot to
    /// call it — and the reason the constraint is worth having at all.
    /// </summary>
    [Fact]
    public async Task Phase14_TheDatabaseRefusesALinkThatDoesNotNameExactlyOneTarget()
    {
        await using var conn = await ApplyPhase14Async("check");

        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.table_constraints "
            + "WHERE constraint_schema = DATABASE() AND table_name = 'finding_issue_links' "
            + "AND constraint_name = 'ck_finding_issue_links_one_target'"));

        await ExecuteAsync(conn,
            "INSERT INTO issue_tracker_connections (id, name, provider, base_url, project_key, "
            + "enabled, push_finding_updates, poll_interval_minutes, created_at) "
            + "VALUES (1, 'Jira', 1, 'https://acme.atlassian.net', 'SD', 1, 1, 15, NOW())");

        await ExecuteAsync(conn,
            "INSERT INTO hosts (Id, HostName, Source, RegistrationDate, Status) "
            + "VALUES (1, 'srv-01', 'manual', NOW(), 1)");

        await ExecuteAsync(conn,
            "INSERT INTO vulnerabilities (Id, Title, Severity, FirstDetection, LastDetection, "
            + "Status, HostId, DetectionCount) "
            + "VALUES (42, 'SQL injection', 'critical', NOW(), NOW(), 1, 1, 1)");

        // An incident needs a creator, and the numbered schema seeds roles but no users. Inserted
        // against the first seeded role, so the FK holds whatever id that happens to be.
        await ExecuteAsync(conn,
            "INSERT INTO `user` (value, enabled, type, login, name, email, password, role_id) "
            + "SELECT 1, 1, 'local', 'analyst', 'analyst', 'analyst@acme.com', "
            + "REPEAT('x', 60), MIN(value) FROM role");

        await ExecuteAsync(conn,
            "INSERT INTO incidents (Id, Year, Sequence, Name, Description, Category, CreationDate, "
            + "LastUpdate, CreatedById, Status) "
            + "VALUES (7, 2026, 7, '2026-0007', 'Outage.', 'availability', NOW(), NOW(), 1, 2)");

        // No target at all.
        await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(conn,
            "INSERT INTO finding_issue_links (connection_id, issue_key, target_kind, "
            + "last_change_from_remote, has_conflict, created_at) "
            + "VALUES (1, 'SD-1', 1, 0, 0, NOW())"));

        // Two targets.
        await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(conn,
            "INSERT INTO finding_issue_links (connection_id, issue_key, target_kind, "
            + "vulnerability_id, incident_id, last_change_from_remote, has_conflict, created_at) "
            + "VALUES (1, 'SD-2', 1, 42, 7, 0, 0, NOW())"));

        // Exactly one is accepted.
        await ExecuteAsync(conn,
            "INSERT INTO finding_issue_links (connection_id, issue_key, target_kind, incident_id, "
            + "last_change_from_remote, has_conflict, created_at) "
            + "VALUES (1, 'SD-3', 2, 7, 0, 0, NOW())");

        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM finding_issue_links WHERE issue_key = 'SD-3'"));
    }

    /// <summary>
    /// The mirror cannot hold two copies of one ticket, and SLA is keyed per *cycle*.
    ///
    /// The second is the subtle one: a reopened request starts a second cycle of the same metric, so
    /// keying on (request, metric) alone would overwrite the first cycle's breach with the second
    /// cycle's clean state and the breach would vanish from the record.
    /// </summary>
    [Fact]
    public async Task Phase14_TheMirrorIsUniquePerTicketAndSlaIsUniquePerCycle()
    {
        await using var conn = await ApplyPhase14Async("uniques");

        Assert.Equal("2", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() "
            + "AND table_name = 'jira_service_requests' "
            + "AND index_name = 'uq_jira_service_requests_connection_key' AND non_unique = 0"));

        Assert.Equal("3", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() "
            + "AND table_name = 'jira_request_slas' "
            + "AND index_name = 'uq_jira_request_slas_request_metric_cycle' AND non_unique = 0"));
    }

    /// <summary>
    /// Deleting a host does not delete the record that Jira reported it.
    ///
    /// <c>SET NULL</c> rather than <c>CASCADE</c>, because "this Assets object mapped to a host that
    /// has since been removed" is exactly the row somebody needs when the machine reappears on the
    /// next import.
    /// </summary>
    [Fact]
    public async Task Phase14_TheAssetAuditSurvivesDeletingItsTarget()
    {
        await using var conn = await ApplyPhase14Async("audit");

        Assert.Equal("2", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.referential_constraints "
            + "WHERE constraint_schema = DATABASE() "
            + "AND constraint_name IN ('fk_jira_asset_objects_target_host_id', "
            + "'fk_jira_asset_objects_target_entity_id') AND delete_rule = 'SET NULL'"));

        await ExecuteAsync(conn,
            "INSERT INTO issue_tracker_connections (id, name, provider, base_url, project_key, "
            + "enabled, push_finding_updates, poll_interval_minutes, created_at) "
            + "VALUES (1, 'Jira', 1, 'https://acme.atlassian.net', 'SD', 1, 1, 15, NOW())");

        await ExecuteAsync(conn,
            "INSERT INTO hosts (Id, HostName, Source, RegistrationDate, Status) "
            + "VALUES (1, 'srv-01', 'JiraAssets', NOW(), 42)");

        await ExecuteAsync(conn,
            "INSERT INTO jira_asset_objects (connection_id, object_id, object_key, target_kind, "
            + "target_host_id, mapped_name, first_seen_at) "
            + "VALUES (1, '1042', 'ITSM-88', 1, 1, 'srv-01', NOW())");

        await ExecuteAsync(conn, "DELETE FROM hosts WHERE Id = 1");

        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM jira_asset_objects WHERE object_id = '1042' "
            + "AND target_host_id IS NULL"));
    }

    /// <summary>
    /// Deleting a connection takes its whole configuration and mirror with it.
    ///
    /// The mirror holds third-party personal data — reporter and owner display names — so it must not
    /// outlive the connection that was authorised to read it.
    /// </summary>
    [Fact]
    public async Task Phase14_DeletingAConnectionRemovesItsFacetMirrorAndMappings()
    {
        await using var conn = await ApplyPhase14Async("cascade");

        await ExecuteAsync(conn,
            "INSERT INTO issue_tracker_connections (id, name, provider, base_url, project_key, "
            + "enabled, push_finding_updates, poll_interval_minutes, created_at) "
            + "VALUES (1, 'Jira', 1, 'https://acme.atlassian.net', 'SD', 1, 1, 15, NOW())");

        await ExecuteAsync(conn,
            "INSERT INTO jira_connection_settings (connection_id, deployment, jsm_enabled, "
            + "import_slas, sla_breach_notifications, default_link_target_kind, assets_enabled, "
            + "created_at) VALUES (1, 1, 1, 1, 0, 1, 1, NOW())");

        await ExecuteAsync(conn,
            "INSERT INTO jira_queue_imports (connection_id, service_desk_id, queue_id, enabled, "
            + "max_requests, created_at) VALUES (1, 3, 10, 1, 500, NOW())");

        await ExecuteAsync(conn,
            "INSERT INTO jira_service_requests (connection_id, issue_key, is_closed, first_seen_at) "
            + "VALUES (1, 'SD-4711', 0, NOW())");

        await ExecuteAsync(conn,
            "INSERT INTO jira_request_slas (request_id, metric_name, is_ongoing, breached, paused, "
            + "captured_at) SELECT id, 'Time to first response', 1, 1, 0, NOW() "
            + "FROM jira_service_requests WHERE issue_key = 'SD-4711'");

        await ExecuteAsync(conn,
            "INSERT INTO jira_object_mappings (connection_id, object_type_id, object_type_name, "
            + "target_kind, match_strategy, enabled, create_missing, update_existing, "
            + "deactivate_missing, created_at) VALUES (1, 23, 'Server', 1, 0, 1, 1, 1, 0, NOW())");

        await ExecuteAsync(conn,
            "INSERT INTO jira_object_attribute_mappings (mapping_id, source_attribute_name, "
            + "target_field, transform, is_identity, sort_order) "
            + "SELECT id, 'Hostname', 'Name', 0, 1, 0 FROM jira_object_mappings WHERE connection_id = 1");

        await ExecuteAsync(conn, "DELETE FROM issue_tracker_connections WHERE id = 1");

        foreach (var table in new[]
                 {
                     "jira_connection_settings", "jira_queue_imports", "jira_service_requests",
                     "jira_request_slas", "jira_object_mappings", "jira_object_attribute_mappings"
                 })
            Assert.Equal("0", await ScalarAsync(conn, $"SELECT COUNT(*) FROM {table}"));
    }

    /// <summary>
    /// The Structure script is safe to apply twice.
    ///
    /// MariaDB implicitly commits every DDL statement, so a failure part-way through leaves the
    /// database between versions; converging on a second pass is what makes the retry possible rather
    /// than a hand-written repair. <c>SchemaUpgradeIdempotenceTest</c> checks the convention statement
    /// by statement without a database; this checks the actual outcome — including the guarded
    /// <c>CHECK</c> constraint, whose probe is the one guard that is not a native MariaDB clause.
    /// </summary>
    [Fact]
    public async Task Phase14_TheStructureScriptConvergesWhenAppliedTwice()
    {
        await using var conn = await ApplyPhase14Async("retry");

        var structure = Path.Combine(MariaDbContainerFixture.RepoDbDir(), "Structure", "83.sql");

        await MariaDbContainerFixture.ExecAsync(conn, await File.ReadAllTextAsync(structure));

        Assert.Equal("8", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() "
            + "AND table_name IN ('jira_connection_settings', 'jira_queue_imports', "
            + "'jira_service_requests', 'jira_request_slas', 'jira_field_mappings', "
            + "'jira_object_mappings', 'jira_object_attribute_mappings', 'jira_asset_objects')"));

        // Still exactly one — a second unguarded ADD CONSTRAINT would have failed the script outright.
        Assert.Equal("1", await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.table_constraints "
            + "WHERE constraint_schema = DATABASE() AND table_name = 'finding_issue_links' "
            + "AND constraint_name = 'ck_finding_issue_links_one_target'"));
    }
}
