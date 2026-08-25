using System.Text;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Proves the property the numbered upgrade scripts are written for: a version that failed part-way
/// can simply be applied again.
///
/// MariaDB implicitly commits every DDL statement, so no transaction can undo a half-applied
/// Structure script — the upgrade that stranded a production database at <c>db_version</c> 76 with
/// two-thirds of version 77 already committed had no rollback available at all. What replaces it is
/// that every statement is guarded, so a second pass skips what already landed. This test applies
/// each Structure script twice against a real MariaDB and requires the resulting schema to be
/// indistinguishable from a single clean pass — column for column, index for index, key for key.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class SchemaUpgradeRetryTests(MariaDbContainerFixture fixture)
{
    [Fact]
    public async Task TestApplyingEveryStructureScriptTwiceLeavesTheSameSchema()
    {
        var target = MariaDbContainerFixture.TargetSchemaVersion;

        await fixture.InitializeNumberedSchemaAsync(target);
        var clean = await FingerprintAsync();
        var cleanSeed = await SeedCountsAsync();

        await fixture.InitializeNumberedSchemaAsync(target, structureApplications: 2);
        var retried = await FingerprintAsync();
        var retriedSeed = await SeedCountsAsync();

        Assert.Equal(clean, retried);
        Assert.Equal(cleanSeed, retriedSeed);
    }

    /// <summary>The retry must also land on the target version, not stall short of it.</summary>
    [Fact]
    public async Task TestRetriedUpgradeStillReachesTheTargetVersion()
    {
        var target = MariaDbContainerFixture.TargetSchemaVersion;

        await fixture.InitializeNumberedSchemaAsync(target, structureApplications: 2);

        await using var connection = new MySqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand("SELECT `value` FROM `settings` WHERE `name` = 'db_version';", connection);

        Assert.Equal(target.ToString(), (string?)await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// Every column, index and foreign key in the schema, ordered deterministically and compared with
    /// a case-sensitive collation — the Track 6 renames are case-only in places (<c>Incidents</c> to
    /// <c>incidents</c>), and a case-insensitive comparison would call a skipped rename a match.
    /// </summary>
    private async Task<string> FingerprintAsync()
    {
        var builder = new StringBuilder();

        await AppendAsync(builder, """
            SELECT CONCAT_WS('|', TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE,
                                  IFNULL(COLUMN_DEFAULT, '~'), EXTRA, IFNULL(COLLATION_NAME, '~'))
              FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()
             ORDER BY BINARY TABLE_NAME, BINARY COLUMN_NAME
            """);

        await AppendAsync(builder, """
            SELECT CONCAT_WS('|', TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX, COLUMN_NAME, NON_UNIQUE, INDEX_TYPE)
              FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE()
             ORDER BY BINARY TABLE_NAME, BINARY INDEX_NAME, SEQ_IN_INDEX
            """);

        await AppendAsync(builder, """
            SELECT CONCAT_WS('|', CONSTRAINT_NAME, TABLE_NAME, COLUMN_NAME,
                                  IFNULL(REFERENCED_TABLE_NAME, '~'), IFNULL(REFERENCED_COLUMN_NAME, '~'))
              FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = DATABASE()
             ORDER BY BINARY CONSTRAINT_NAME, BINARY TABLE_NAME, ORDINAL_POSITION
            """);

        return builder.ToString();
    }

    /// <summary>Seed rows, so a guard that made a script re-insert instead of skip is caught too.</summary>
    private async Task<string> SeedCountsAsync()
    {
        var builder = new StringBuilder();

        await AppendAsync(builder, """
            SELECT CONCAT_WS('|', 'settings', COUNT(*)) FROM `settings`
            UNION ALL SELECT CONCAT_WS('|', 'migrations', COUNT(*)) FROM `__EFMigrationsHistory`
            UNION ALL SELECT CONCAT_WS('|', 'sla', COUNT(*)) FROM `sla_configurations`
            UNION ALL SELECT CONCAT_WS('|', 'dedup', COUNT(*)) FROM `scanner_dedup_configurations`
            UNION ALL SELECT CONCAT_WS('|', 'orphans', COUNT(*)) FROM `schema_upgrade_orphans`
            UNION ALL SELECT CONCAT_WS('|', 'category', COUNT(*)) FROM `category`
            """);

        return builder.ToString();
    }

    private async Task AppendAsync(StringBuilder builder, string sql)
    {
        await using var connection = new MySqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync()) builder.AppendLine(reader.GetString(0));
    }
}
