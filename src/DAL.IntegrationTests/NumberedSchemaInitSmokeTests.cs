using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Smoke test that the full numbered-SQL schema (Structure/Data 1..63) applies cleanly to a real
/// MySQL 8 container — the faithful test bed for Track 6 phases. Also asserts the legacy tables a
/// phase will touch actually exist and that db_version lands at 63.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class NumberedSchemaInitSmokeTests(MariaDbContainerFixture fixture)
{
    [Fact]
    public async Task NumberedSchema_AppliesCleanly_To63()
    {
        await fixture.InitializeNumberedSchemaAsync(63);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        foreach (var table in new[] { "settings", "mgmt_reviews", "mitigations", "comments", "framework_controls", "BiometricTransaction", "schema_upgrade_log" })
        {
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @t", conn);
            cmd.Parameters.AddWithValue("@t", table);
            Assert.True(Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1, $"expected table '{table}' to exist after init");
        }

        await using var verCmd = new MySqlCommand("SELECT value FROM settings WHERE name='db_version'", conn);
        Assert.Equal("63", (await verCmd.ExecuteScalarAsync())?.ToString());
    }
}
