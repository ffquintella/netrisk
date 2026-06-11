using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Validates the shipped <c>schema_upgrade_log</c> DDL (<c>Structure/63.sql</c>, generated from EF
/// migration <c>20260611141630_SchemaUpgradeLog</c>) against a real MySQL, and that the EF entity
/// mapping round-trips against it. Covers the 6.1 spec's "additive migration round-trip" test.
/// </summary>
[Collection("mysql")]
[Trait("Category", "Integration")]
public class SchemaUpgradeLogMigrationTests(MySqlContainerFixture fixture)
{
    [Fact]
    public async Task ShippedDdl_CreatesTableAndIndex()
    {
        await fixture.ResetMinimalSchemaAsync();

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await using var tableCmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables " +
            "WHERE table_schema = DATABASE() AND table_name = 'schema_upgrade_log'", conn);
        Assert.Equal(1, Convert.ToInt64(await tableCmd.ExecuteScalarAsync()));

        await using var idxCmd = new MySqlCommand(
            "SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics " +
            "WHERE table_schema = DATABASE() AND table_name = 'schema_upgrade_log' " +
            "AND index_name = 'idx_schema_upgrade_log_phase_status'", conn);
        Assert.Equal(1, Convert.ToInt64(await idxCmd.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SchemaUpgradeLog_EfInsertAndRead_RoundTrips()
    {
        await fixture.ResetMinimalSchemaAsync();

        await using (var context = fixture.NewContext())
        {
            context.SchemaUpgradeLogs.Add(new SchemaUpgradeLog
            {
                Phase = "1", StartVersion = 63, TargetVersion = 64, Status = "Success",
                Environment = "homolog", Operator = "itest",
                BackupPath = "/tmp/backup.sql", BackupHash = "ABC123",
                Notes = "round-trip", AppliedAt = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc)
            });
            await context.SaveChangesAsync();
        }

        await using var verify = fixture.NewContext();
        var read = await verify.SchemaUpgradeLogs.SingleAsync(l => l.Phase == "1" && l.Operator == "itest");
        Assert.Equal(63, read.StartVersion);
        Assert.Equal(64, read.TargetVersion);
        Assert.Equal("Success", read.Status);
        Assert.Equal("ABC123", read.BackupHash);
    }
}
