using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.9 (S50 §5, §8 Q1–Q3) — the archive, backtesting and committee version on a real MariaDB: the nine tables
/// arrive, the third-line permission and role are seeded once and a retry converges without duplicating them; the CHECKs
/// and unique indexes refuse what the services refuse — in particular one vote per member and decision, one backtest per
/// incident, one condition per archive and type, which the in-memory provider cannot enforce; and deleting a risk, an
/// incident, a closure, a reassessment event or a user removes or releases exactly what the model says.
///
/// The version is found by its marker, never written here (S42 §11, defect 1). Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9DecisionCycleSchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`risk_committee_votes`");

    private static readonly string[] Tables =
    [
        "risk_archives", "risk_archive_conditions", "risk_archive_reviews", "incident_backtests", "incident_backtest_risks",
        "risk_committees", "risk_committee_members", "risk_committee_decisions", "risk_committee_votes"
    ];

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static Task ExecAsync(MySqlConnection conn, string sql) => MariaDbContainerFixture.ExecAsync(conn, sql);

    private static async Task<long> CountAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task ApplyVersionAsync(MySqlConnection conn, int version, params string[] parts)
    {
        foreach (var part in parts.Length == 0 ? ["Structure", "Data"] : parts)
            await ExecAsync(conn, await File.ReadAllTextAsync(
                Path.Combine(MariaDbContainerFixture.RepoDbDir(), part, $"{version}.sql")));
    }

    private static async Task<MySqlException> RefusedAsync(MySqlConnection conn, string sql) =>
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, sql));

    /// <summary>Users 990/991, risks 9911/9912, a closure 9931 of risk 9911, an incident 9921 and a committee 9941 with 990 as member.</summary>
    private static Task SeedAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
        "VALUES (990,1,0,'local','U990','u990@x.test','s',REPEAT('x',60),1,0,'user990')," +
        "(991,1,0,'local','U991','u991@x.test','s',REPEAT('x',60),1,0,'user991');" +
        "INSERT INTO `risks` (`id`,`status`) VALUES (9911,'Closed'),(9912,'New');" +
        "INSERT INTO closures (id, risk_id, user_id, closure_date, close_reason, note) VALUES (9931, 9911, 990, NOW(), 4, 'x');" +
        "INSERT INTO incidents (Id, Year, Sequence, Name, Description, Category, CreationDate, LastUpdate, CreatedById, Status) " +
        "VALUES (9921, 2026, 9921, '2026-9921', 'Ransomware.', 'malware', NOW(), NOW(), 991, 2);" +
        "INSERT INTO risk_committees (id, name, required_approvals, created_at) VALUES (9941, 'IT Risk Committee', 2, NOW());" +
        "INSERT INTO risk_committee_members (committee_id, user_id, created_at) VALUES (9941, 990, NOW());");

    private static string ArchiveRow(int id, int status = 1, string reopenedAt = "NULL", string origin = "NULL",
        string reason = "NULL") =>
        "INSERT INTO risk_archives (id, risk_id, closure_id, status, justification, previous_status, archived_at, " +
        "archived_by_id, next_review_due_at, reopened_at, reopen_origin, reopen_reason, created_at) VALUES " +
        $"({id}, 9911, 9931, {status}, 'Not worth treating', 'New', NOW(), 990, NOW() + INTERVAL 3 MONTH, {reopenedAt}, " +
        $"{origin}, {reason}, NOW())";

    private static string DecisionRow(int id, int status = 1, string closedAt = "NULL", string withdrawal = "NULL",
        int required = 2, int kind = 1) =>
        "INSERT INTO risk_committee_decisions (id, committee_id, risk_id, kind, status, required_approvals, " +
        "business_justification, expires_at, opened_by_id, opened_at, closed_at, withdrawal_reason, created_at, version) VALUES " +
        $"({id}, 9941, 9912, {kind}, {status}, {required}, 'Insured', NOW() + INTERVAL 90 DAY, 991, NOW(), {closedAt}, " +
        $"{withdrawal}, NOW(), 0)";

    /// <summary>
    /// Q1 — the upgrade creates the nine tables empty, seeds the third-line permission and role with their grants once,
    /// and a replay (Structure twice, Data again) converges without a second row.
    /// </summary>
    [Fact]
    public async Task TestQ1_TheUpgradeCreatesTheSchemaSeedsTheThirdLineOnceAndConverges()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();

        await ApplyVersionAsync(conn, V);
        await ExecAsync(conn, $"UPDATE settings SET value = '{V - 1}' WHERE name = 'db_version';");
        await ApplyVersionAsync(conn, V, "Structure");
        await ApplyVersionAsync(conn, V, "Structure");
        await ApplyVersionAsync(conn, V, "Data");

        Assert.Equal((long)V, await CountAsync(conn, "SELECT CAST(value AS UNSIGNED) FROM settings WHERE name = 'db_version'"));

        foreach (var table in Tables)
        {
            Assert.Equal(1L, await CountAsync(conn,
                $"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}'"));
            Assert.Equal(0L, await CountAsync(conn, $"SELECT COUNT(*) FROM `{table}`"));
        }

        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM permissions WHERE `key` = 'third_line_assurance'"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM role WHERE name = 'ThirdLineAuditor'"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM role_responsibilities rr JOIN role r ON r.value = rr.role_id " +
            "JOIN permissions p ON p.id = rr.permission_id WHERE r.name = 'ThirdLineAuditor' AND p.`key` = 'third_line_assurance'"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM role_responsibilities rr JOIN role r ON r.value = rr.role_id " +
            "JOIN permissions p ON p.id = rr.permission_id WHERE r.name = 'ThirdLineAuditor' AND p.`key` = 'riskmanagement'"));
        Assert.Equal(0L, await CountAsync(conn,
            "SELECT COUNT(*) FROM role_responsibilities rr JOIN role r ON r.value = rr.role_id " +
            "JOIN permissions p ON p.id = rr.permission_id WHERE r.name = 'ThirdLineAuditor' " +
            "AND p.`key` IN ('submit_risks', 'modify_risks', 'close_risks', 'delete_risk', 'review_high', 'review_veryhigh')"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9DecisionCycle'"));
    }

    /// <summary>Q2 — the CHECKs and unique indexes refuse what the services refuse.</summary>
    [Fact]
    public async Task TestQ2_ConstraintsRefuseWhatTheServicesRefuse()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        // Archives: a status 3; reopened without its date, origin or reason; archived carrying a reopening.
        await RefusedAsync(conn, ArchiveRow(9951, status: 3));
        await RefusedAsync(conn, ArchiveRow(9952, status: 2));
        await RefusedAsync(conn, ArchiveRow(9953, status: 2, reopenedAt: "NOW()", origin: "1"));
        await RefusedAsync(conn, ArchiveRow(9954, status: 1, reopenedAt: "NOW()", origin: "1", reason: "'x'"));
        await RefusedAsync(conn, ArchiveRow(9955, status: 2, reopenedAt: "NOW()", origin: "4", reason: "'x'"));
        await ExecAsync(conn, ArchiveRow(9950));

        // One condition per archive and type; type 7 is no trigger.
        await ExecAsync(conn, "INSERT INTO risk_archive_conditions (archive_id, trigger_type, created_at) VALUES (9950, 4, NOW())");
        var condition = await RefusedAsync(conn,
            "INSERT INTO risk_archive_conditions (archive_id, trigger_type, created_at) VALUES (9950, 4, NOW())");
        Assert.Contains("uq_risk_archive_conditions_archive_id_trigger_type", condition.Message);
        await RefusedAsync(conn, "INSERT INTO risk_archive_conditions (archive_id, trigger_type, created_at) VALUES (9950, 7, NOW())");

        // A review that keeps the archive names its next date; one that reopens does not.
        await RefusedAsync(conn,
            "INSERT INTO risk_archive_reviews (archive_id, outcome, note, reviewed_at, next_review_due_at, created_at) VALUES (9950, 1, 'x', NOW(), NULL, NOW())");
        await RefusedAsync(conn,
            "INSERT INTO risk_archive_reviews (archive_id, outcome, note, reviewed_at, next_review_due_at, created_at) VALUES (9950, 2, 'x', NOW(), NOW(), NOW())");

        // One backtest per incident, one match per backtest and risk.
        await ExecAsync(conn, "INSERT INTO incident_backtests (id, incident_id, assessed_at, created_at) VALUES (9961, 9921, NOW(), NOW())");
        Assert.Contains("uq_incident_backtests_incident_id", (await RefusedAsync(conn,
            "INSERT INTO incident_backtests (incident_id, assessed_at, created_at) VALUES (9921, NOW(), NOW())")).Message);
        await ExecAsync(conn, "INSERT INTO incident_backtest_risks (backtest_id, risk_id, created_at) VALUES (9961, 9911, NOW())");
        Assert.Contains("uq_incident_backtest_risks_backtest_id_risk_id", (await RefusedAsync(conn,
            "INSERT INTO incident_backtest_risks (backtest_id, risk_id, created_at) VALUES (9961, 9911, NOW())")).Message);

        // Committees decide with two to fifty approvals; one member once.
        await RefusedAsync(conn, "INSERT INTO risk_committees (name, required_approvals, created_at) VALUES ('Solo', 1, NOW())");
        Assert.Contains("uq_risk_committee_members_committee_id_user_id", (await RefusedAsync(conn,
            "INSERT INTO risk_committee_members (committee_id, user_id, created_at) VALUES (9941, 990, NOW())")).Message);

        // Decisions: kind 3, status 5, one approval, closed without a date, open with one, withdrawn without a reason.
        await RefusedAsync(conn, DecisionRow(9971, kind: 3));
        await RefusedAsync(conn, DecisionRow(9972, status: 5, closedAt: "NOW()"));
        await RefusedAsync(conn, DecisionRow(9973, required: 1));
        await RefusedAsync(conn, DecisionRow(9974, status: 2));
        await RefusedAsync(conn, DecisionRow(9975, status: 1, closedAt: "NOW()"));
        await RefusedAsync(conn, DecisionRow(9976, status: 4, closedAt: "NOW()"));
        await ExecAsync(conn, DecisionRow(9970));

        // One vote per member and decision — two racing for the last approval cannot both count; choice 4 is no vote.
        await ExecAsync(conn, "INSERT INTO risk_committee_votes (decision_id, voter_id, choice, cast_at, created_at) VALUES (9970, 990, 1, NOW(), NOW())");
        Assert.Contains("uq_risk_committee_votes_decision_id_voter_id", (await RefusedAsync(conn,
            "INSERT INTO risk_committee_votes (decision_id, voter_id, choice, cast_at, created_at) VALUES (9970, 990, 2, NOW(), NOW())")).Message);
        await RefusedAsync(conn, "INSERT INTO risk_committee_votes (decision_id, voter_id, choice, cast_at, created_at) VALUES (9970, 991, 4, NOW(), NOW())");
    }

    /// <summary>
    /// Q3 — deleting the closure only detaches the archive (the legacy reopen route); deleting a user only clears who
    /// wrote; deleting the incident removes its backtest; deleting a risk removes its archives, matches and decisions.
    /// </summary>
    [Fact]
    public async Task TestQ3_DeletesCascadeOrReleaseAsDeclared()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);
        await ExecAsync(conn, ArchiveRow(9950));
        await ExecAsync(conn, "INSERT INTO incident_backtests (id, incident_id, assessed_at, assessed_by_id, created_at) VALUES (9961, 9921, NOW(), 990, NOW())");
        await ExecAsync(conn, "INSERT INTO incident_backtest_risks (backtest_id, risk_id, created_at) VALUES (9961, 9912, NOW())");
        await ExecAsync(conn, DecisionRow(9970));
        await ExecAsync(conn, "INSERT INTO risk_committee_votes (decision_id, voter_id, choice, cast_at, created_at) VALUES (9970, 990, 1, NOW(), NOW())");

        await ExecAsync(conn, "DELETE FROM closures WHERE id = 9931");
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_archives WHERE id = 9950 AND closure_id IS NULL"));

        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 990");
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_archives WHERE id = 9950 AND archived_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_committee_votes WHERE voter_id IS NULL"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_committee_members WHERE user_id = 990"));

        await ExecAsync(conn, "DELETE FROM incidents WHERE Id = 9921");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM incident_backtests"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM incident_backtest_risks"));

        await ExecAsync(conn, "DELETE FROM risks WHERE id IN (9911, 9912)");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_archives"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_committee_decisions"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_committee_votes"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_committees"));
    }
}
