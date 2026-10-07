using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.7 (S48 §5, §8 Q1–Q3) — the tail-statistics version on a real MariaDB: the five tables arrive with nothing
/// seeded and a retry converges; the CHECKs and unique indexes refuse what the service refuses; deleting a risk, an
/// appetite or a tail run removes what hangs off it, and deleting a user only clears who wrote it.
///
/// The version is found by its marker, never written here (S42 §11, defect 1).
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9TailRiskSchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`risk_tail_statistics`");

    private static readonly string[] Tables =
    [
        "risk_loss_components", "risk_tail_statistics", "risk_tail_components", "risk_correlations",
        "risk_appetite_tail_limits"
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

    /// <summary>A user (970), two risks and an appetite.</summary>
    private static Task SeedAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
        "VALUES (970,1,0,'local','U970','u970@x.test','s',REPEAT('x',60),1,0,'user970');" +
        "INSERT INTO `risks` (`id`,`status`) VALUES (9701,'New'),(9702,'New');" +
        "INSERT INTO `risk_appetites` (`id`,`entity_id`,`max_acceptable_residual`,`dual_approval_threshold`,`created_at`) " +
        "VALUES (9790,NULL,6,5,NOW());");

    private const string TailColumns =
        "(risk_id, run, iterations, seed, confidence_level, lef_min, lef_most_likely, lef_max, magnitude_min, " +
        "magnitude_most_likely, magnitude_max, magnitude_source, mitigation_effectiveness, expected_loss, " +
        "expected_loss_ci_low, expected_loss_ci_high, p95, p95_ci_low, p95_ci_high, cvar95, cvar95_ci_low, cvar95_ci_high, " +
        "probability_of_loss, conditional_loss, computed_at, created_at)";

    private static string TailRow(int riskId, int run, int iterations = 10000, double probability = 0.03,
        int source = 1, double effectiveness = 0) =>
        $"INSERT INTO risk_tail_statistics {TailColumns} VALUES ({riskId}, {run}, {iterations}, 7, 0.950, 0.01, 0.03, 0.06, " +
        $"1000000, 4000000, 10000000, {source}, {effectiveness}, 137000, 120000, 154000, 0, 0, 0, 2740000, 2400000, " +
        $"3080000, {probability}, 4700000, NOW(), NOW())";

    private static async Task<MySqlException> RefusedAsync(MySqlConnection conn, string sql) =>
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, sql));

    /// <summary>Q1 — the upgrade creates the five tables, seeds nothing, and a replay (Structure twice, Data again) converges.</summary>
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

        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9TailRisk'"));
        Assert.Equal(0L, await CountAsync(conn,
            "SELECT COUNT(*) FROM settings WHERE name IN ('tail_flag_max_annual_probability', 'tail_flag_catastrophic_loss')"));
    }

    /// <summary>Q2 — the CHECKs and unique indexes refuse what the service refuses.</summary>
    [Fact]
    public async Task TestQ2_ConstraintsRefuseWhatTheServiceRefuses()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        // Components: undefined, unordered, a fine without its legal basis, a component twice.
        await RefusedAsync(conn,
            "INSERT INTO risk_loss_components (risk_id, component, loss_min, loss_most_likely, loss_max, created_at) VALUES (9701, 8, 0, 1, 2, NOW())");
        await RefusedAsync(conn,
            "INSERT INTO risk_loss_components (risk_id, component, loss_min, loss_most_likely, loss_max, created_at) VALUES (9701, 1, 5, 1, 2, NOW())");
        await RefusedAsync(conn,
            "INSERT INTO risk_loss_components (risk_id, component, loss_min, loss_most_likely, loss_max, created_at) VALUES (9701, 6, 0, 1, 2, NOW())");
        await ExecAsync(conn,
            "INSERT INTO risk_loss_components (risk_id, component, loss_min, loss_most_likely, loss_max, basis, created_at) VALUES (9701, 6, 0, 1, 2, 'LGPD art. 52', NOW())");
        var twice = await RefusedAsync(conn,
            "INSERT INTO risk_loss_components (risk_id, component, loss_min, loss_most_likely, loss_max, basis, created_at) VALUES (9701, 6, 0, 1, 2, 'x', NOW())");
        Assert.Contains("uq_risk_loss_components_risk_id_component", twice.Message);

        // Tail statistics: run 3, too few iterations, a probability above 1, an effectiveness above 1, a run twice.
        await RefusedAsync(conn, TailRow(9701, 3));
        await RefusedAsync(conn, TailRow(9701, 1, iterations: 500));
        await RefusedAsync(conn, TailRow(9701, 1, probability: 1.5));
        await RefusedAsync(conn, TailRow(9701, 2, effectiveness: 1.5));
        await ExecAsync(conn, TailRow(9701, 1));
        await RefusedAsync(conn, TailRow(9701, 1));

        // Correlations: an inverted pair, a coefficient above 1.
        await RefusedAsync(conn,
            "INSERT INTO risk_correlations (risk_a_id, risk_b_id, coefficient, rationale, created_at) VALUES (9702, 9701, 0.5, 'x', NOW())");
        await RefusedAsync(conn,
            "INSERT INTO risk_correlations (risk_a_id, risk_b_id, coefficient, rationale, created_at) VALUES (9701, 9702, 1.5, 'x', NOW())");

        // Tolerances: none set, a negative one.
        await RefusedAsync(conn,
            "INSERT INTO risk_appetite_tail_limits (appetite_id, rationale, created_at) VALUES (9790, 'x', NOW())");
        await RefusedAsync(conn,
            "INSERT INTO risk_appetite_tail_limits (appetite_id, max_scenario_p95, rationale, created_at) VALUES (9790, -1, 'x', NOW())");
    }

    /// <summary>Q3 — deleting the user clears who wrote; deleting a risk, a tail run or an appetite removes what hangs off it.</summary>
    [Fact]
    public async Task TestQ3_DeletesCascadeAndAuthorsAreCleared()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        await ExecAsync(conn,
            "INSERT INTO risk_loss_components (risk_id, component, loss_min, loss_most_likely, loss_max, created_at, updated_by_id) VALUES (9701, 1, 0, 1, 2, NOW(), 970);" +
            "INSERT INTO risk_correlations (risk_a_id, risk_b_id, coefficient, rationale, created_at, updated_by_id) VALUES (9701, 9702, 0.5, 'x', NOW(), 970);" +
            "INSERT INTO risk_appetite_tail_limits (appetite_id, max_scenario_cvar95, rationale, created_at, updated_by_id) VALUES (9790, 1000000, 'x', NOW(), 970);" +
            TailRow(9701, 1, source: 2) + ";" +
            "INSERT INTO risk_tail_components (tail_statistics_id, component, loss_min, loss_most_likely, loss_max, expected_loss, cvar95, created_at) " +
            "SELECT id, 1, 0, 1, 2, 10, 20, NOW() FROM risk_tail_statistics WHERE risk_id = 9701;");

        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 970");
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_loss_components WHERE updated_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_correlations WHERE updated_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_appetite_tail_limits WHERE updated_by_id IS NULL"));

        await ExecAsync(conn, "DELETE FROM risk_appetites WHERE id = 9790;");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_appetite_tail_limits"));

        // The second risk's deletion removes the pair; the first's, its components, its runs and their components.
        await ExecAsync(conn, "DELETE FROM risks WHERE id = 9702;");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_correlations"));

        await ExecAsync(conn, "DELETE FROM risks WHERE id = 9701;");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_loss_components"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_tail_statistics"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_tail_components"));
    }
}
