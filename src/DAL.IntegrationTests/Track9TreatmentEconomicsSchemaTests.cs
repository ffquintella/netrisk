using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.6 (S47 §5, §8 Q1–Q3) — the treatment-economics version on a real MariaDB: the three tables and five columns
/// arrive with nothing seeded and a retry converges; the CHECKs refuse what the service refuses; deleting a mitigation
/// or a risk removes its economics, dependencies and target, and deleting a user only clears who wrote them.
///
/// The version is found by its marker, never written here (S42 §11, defect 1).
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9TreatmentEconomicsSchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`mitigation_economics`");

    private static readonly string[] Tables = ["mitigation_economics", "mitigation_dependencies", "risk_targets"];

    private static readonly (string Table, string Column)[] Columns =
    [
        ("mitigation_tasks", "acceptance_criterion"), ("mitigation_tasks", "completion_evidence"),
        ("mitigation_tasks", "completion_evidence_at"), ("mitigation_tasks", "completion_evidence_by_id"),
        ("risk_scoring", "quant_residual_ale_mean")
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

    /// <summary>Two users (960 writes the Stage 9.6 rows, 961 owns the mitigations), two risks, a mitigation on each and a task.</summary>
    private static Task SeedAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
        "VALUES (960,1,0,'local','U960','u960@x.test','s',REPEAT('x',60),1,0,'user960')," +
        "(961,1,0,'local','U961','u961@x.test','s',REPEAT('x',60),1,0,'user961');" +
        "INSERT INTO `risks` (`id`,`status`) VALUES (9601,'New'),(9602,'New');" +
        "INSERT INTO `mitigations` (`id`,`risk_id`,`planning_strategy`,`mitigation_effort`,`mitigation_cost`," +
        "`mitigation_owner`,`current_solution`,`security_requirements`,`security_recommendations`,`submitted_by`," +
        "`planning_date`,`mitigation_percent`) VALUES " +
        "(9611,9601,1,1,1,961,'','','',961,'2026-06-01',50),(9612,9602,1,1,1,961,'','','',961,'2026-06-01',50);" +
        "INSERT INTO `mitigation_tasks` (`mitigation_id`,`title`,`status`,`created_at`,`completion_evidence`," +
        "`completion_evidence_by_id`) VALUES (9611,'Enforce MFA',3,NOW(),'Change 1',960);");

    private static async Task<MySqlException> RefusedAsync(MySqlConnection conn, string sql) =>
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, sql));

    /// <summary>Q1 — the upgrade creates the tables and columns, seeds nothing, and a replay (Structure twice, Data again) converges.</summary>
    [Fact]
    public async Task TestQ1_TheUpgradeCreatesTheSchemaSeedsNothingAndConverges()
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

        foreach (var (table, column) in Columns)
            Assert.Equal(1L, await CountAsync(conn,
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                $"AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'"));

        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9TreatmentEconomics'"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() " +
            "AND CONSTRAINT_NAME = 'fk_mitigation_tasks_completion_evidence_by_id'"));
    }

    /// <summary>Q2 — the CHECKs and unique indexes refuse what the service refuses.</summary>
    [Fact]
    public async Task TestQ2_ConstraintsRefuseWhatTheServiceRefuses()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        // An undefined option, a negative or incomplete cost, a horizon out of range.
        await RefusedAsync(conn, "INSERT INTO mitigation_economics (mitigation_id, treatment_option, created_at) VALUES (9611, 5, NOW())");
        await RefusedAsync(conn,
            "INSERT INTO mitigation_economics (mitigation_id, treatment_option, cost_one_time, cost_annual, cost_side_effects_annual, created_at) " +
            "VALUES (9611, 2, 0, -1, 0, NOW())");
        await RefusedAsync(conn,
            "INSERT INTO mitigation_economics (mitigation_id, treatment_option, cost_annual, created_at) VALUES (9611, 2, 10, NOW())");
        await RefusedAsync(conn,
            "INSERT INTO mitigation_economics (mitigation_id, treatment_option, cost_one_time, cost_annual, cost_side_effects_annual, cost_horizon_years, created_at) " +
            "VALUES (9611, 2, 100, 0, 0, 31, NOW())");

        await ExecAsync(conn, "INSERT INTO mitigation_economics (mitigation_id, treatment_option, created_at) VALUES (9611, 2, NOW())");
        var duplicate = await RefusedAsync(conn,
            "INSERT INTO mitigation_economics (mitigation_id, treatment_option, created_at) VALUES (9611, 1, NOW())");
        Assert.Contains("uq_mitigation_economics_mitigation_id", duplicate.Message);

        // A self-dependency; a duplicate dependency.
        await RefusedAsync(conn, "INSERT INTO mitigation_dependencies (mitigation_id, prerequisite_id, created_at) VALUES (9611, 9611, NOW())");
        await ExecAsync(conn, "INSERT INTO mitigation_dependencies (mitigation_id, prerequisite_id, created_at) VALUES (9611, 9612, NOW())");
        await RefusedAsync(conn, "INSERT INTO mitigation_dependencies (mitigation_id, prerequisite_id, created_at) VALUES (9611, 9612, NOW())");

        // A target with no level, out of the scale, or negative.
        await RefusedAsync(conn, "INSERT INTO risk_targets (risk_id, rationale, created_at) VALUES (9601, 'x', NOW())");
        await RefusedAsync(conn, "INSERT INTO risk_targets (risk_id, target_score, rationale, created_at) VALUES (9601, 11, 'x', NOW())");
        await RefusedAsync(conn, "INSERT INTO risk_targets (risk_id, target_expected_loss, rationale, created_at) VALUES (9601, -1, 'x', NOW())");
    }

    /// <summary>Q3 — deleting the user clears who wrote; deleting a mitigation or a risk removes what hangs off it.</summary>
    [Fact]
    public async Task TestQ3_DeletesCascadeAndAuthorsAreCleared()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        await ExecAsync(conn,
            "INSERT INTO mitigation_economics (mitigation_id, treatment_option, created_at, updated_by_id) VALUES (9611, 2, NOW(), 960);" +
            "INSERT INTO mitigation_dependencies (mitigation_id, prerequisite_id, created_at, created_by_id) VALUES (9611, 9612, NOW(), 960);" +
            "INSERT INTO risk_targets (risk_id, target_score, rationale, set_by_id, created_at) VALUES (9601, 3, 'MFA.', 960, NOW());");

        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 960");

        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM mitigation_economics WHERE updated_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM mitigation_dependencies WHERE created_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_targets WHERE set_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM mitigation_tasks WHERE completion_evidence_by_id IS NULL"));

        // The prerequisite's deletion removes the dependency; the mitigation's, its economics; the risk's, its target.
        await ExecAsync(conn, "DELETE FROM mitigations WHERE id = 9612;");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM mitigation_dependencies"));

        await ExecAsync(conn, "DELETE FROM mitigations WHERE id = 9611;");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM mitigation_economics"));

        await ExecAsync(conn, "DELETE FROM risks WHERE id = 9601;");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_targets"));
    }
}
