using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.5 (S46 §5, §8 Q1–Q3) — the flags and Gate A version on a real MariaDB: the two tables arrive with
/// nothing seeded and a retry converges; the CHECKs and the unique index refuse what the service refuses;
/// deleting a risk removes its flags and decisions, and deleting a user only clears who declared or decided.
///
/// The version is found by its marker, never written here (S42 §11, defect 1).
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9RiskFlagsSchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`risk_flags`");

    private static readonly string[] Tables = ["risk_flags", "risk_decisions"];

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

    private static Task SeedAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
        "VALUES (950,1,0,'local','U950','u950@x.test','s',REPEAT('x',60),1,0,'user950');" +
        "INSERT INTO `risks` (`id`,`status`) VALUES (9502,'New');");

    private static async Task<MySqlException> RefusedAsync(MySqlConnection conn, string sql) =>
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, sql));

    /// <summary>Q1 — the upgrade creates both tables, seeds nothing, and a replay (Structure twice, Data again) converges.</summary>
    [Fact]
    public async Task TestQ1_TheUpgradeCreatesTheTablesSeedsNothingAndConverges()
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
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9RiskFlagsGateA'"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND TABLE_NAME = 'risk_flags' AND INDEX_NAME = 'uq_risk_flags_risk_id_flag'"));
    }

    /// <summary>Q2 — the CHECKs refuse an undefined flag, decision or source; the unique index refuses a second row per flag.</summary>
    [Fact]
    public async Task TestQ2_ConstraintsRefuseWhatTheServiceRefuses()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        await RefusedAsync(conn, "INSERT INTO risk_flags (risk_id, flag, created_at) VALUES (9502, 13, NOW())");
        await RefusedAsync(conn, "INSERT INTO risk_flags (risk_id, flag, created_at) VALUES (9502, 0, NOW())");

        await ExecAsync(conn, "INSERT INTO risk_flags (risk_id, flag, declared, created_at) VALUES (9502, 1, 1, NOW())");
        var duplicate = await RefusedAsync(conn, "INSERT INTO risk_flags (risk_id, flag, created_at) VALUES (9502, 1, NOW())");
        Assert.Contains("uq_risk_flags_risk_id_flag", duplicate.Message);

        await RefusedAsync(conn,
            "INSERT INTO risk_decisions (risk_id, decision, source, reason, decided_at, created_at) VALUES (9502, 5, 1, 'x', NOW(), NOW())");
        await RefusedAsync(conn,
            "INSERT INTO risk_decisions (risk_id, decision, source, reason, decided_at, created_at) VALUES (9502, 1, 3, 'x', NOW(), NOW())");
    }

    /// <summary>Q3 — deleting the user clears who declared and decided; deleting the risk removes its flags and decisions.</summary>
    [Fact]
    public async Task TestQ3_DeletesCascadeAndAuthorsAreCleared()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        await ExecAsync(conn,
            "INSERT INTO risk_flags (risk_id, flag, declared, declared_by_id, created_at) VALUES (9502, 1, 1, 950, NOW());" +
            "INSERT INTO risk_decisions (risk_id, decision, source, reason, decided_at, decided_by_id, created_at) " +
            "VALUES (9502, 1, 1, 'Now.', NOW(), 950, NOW());");

        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 950");
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_flags WHERE declared_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_decisions WHERE decided_by_id IS NULL"));

        await ExecAsync(conn, "DELETE FROM risks WHERE id = 9502;");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_flags"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_decisions"));
    }
}
