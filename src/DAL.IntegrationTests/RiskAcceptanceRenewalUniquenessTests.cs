using System.Reflection;
using DAL.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

[Collection("mariadb")]
[Trait("Category", "Integration")]
public class RiskAcceptanceRenewalUniquenessTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("duplicate renewal history exists");

    private async Task<MySqlConnection> OpenAsync()
    {
        var connection = new MySqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static Task ExecAsync(MySqlConnection connection, string sql) =>
        MariaDbContainerFixture.ExecAsync(connection, sql);

    private static async Task SeedUserAndPredecessorAsync(MySqlConnection connection) =>
        await ExecAsync(connection,
            "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
            "VALUES (990,1,0,'local','U990','u990@x.test','s',REPEAT('x',60),1,0,'user990');" +
            "INSERT INTO `risk_acceptances` (`id`,`name`,`authorizing_manager_id`,`expires_at`,`created_at`) " +
            "VALUES (9900,'original',990,'2027-01-01','2026-01-01');");

    [Fact]
    public async Task OneAcceptanceCannotHaveTwoSuccessors()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var connection = await OpenAsync();
        await SeedUserAndPredecessorAsync(connection);

        await ExecAsync(connection,
            "INSERT INTO `risk_acceptances` (`id`,`name`,`authorizing_manager_id`,`expires_at`,`created_at`,`renewed_from_id`) " +
            "VALUES (9901,'renewal one',990,'2027-02-01','2026-02-01',9900);");

        var error = await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(connection,
            "INSERT INTO `risk_acceptances` (`id`,`name`,`authorizing_manager_id`,`expires_at`,`created_at`,`renewed_from_id`) " +
            "VALUES (9902,'renewal two',990,'2027-03-01','2026-03-01',9900);"));

        Assert.Equal(1062, error.Number);
        Assert.Contains("uq_ra_renewed_from_id", error.Message);
    }

    [Fact]
    public async Task UpgradeRefusesPreexistingDuplicateHistoryWithoutChangingIt()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var connection = await OpenAsync();
        await SeedUserAndPredecessorAsync(connection);
        await ExecAsync(connection,
            "INSERT INTO `risk_acceptances` (`id`,`name`,`authorizing_manager_id`,`expires_at`,`created_at`,`renewed_from_id`) VALUES " +
            "(9901,'renewal one',990,'2027-02-01','2026-02-01',9900)," +
            "(9902,'renewal two',990,'2027-03-01','2026-03-01',9900);");

        var script = await File.ReadAllTextAsync(Path.Combine(
            MariaDbContainerFixture.RepoDbDir(), "Structure", $"{V}.sql"));
        var error = await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(connection, script));

        Assert.Contains("duplicate renewal history exists", error.Message);
        await using var count = new MySqlCommand(
            "SELECT COUNT(*) FROM `risk_acceptances` WHERE `renewed_from_id` = 9900", connection);
        Assert.Equal(2L, Convert.ToInt64(await count.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task UpgradeReplacesTheForeignKeySupportingIndexAndCanBeRetried()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var connection = await OpenAsync();
        await SeedUserAndPredecessorAsync(connection);

        var script = await File.ReadAllTextAsync(Path.Combine(
            MariaDbContainerFixture.RepoDbDir(), "Structure", $"{V}.sql"));

        await ExecAsync(connection, script);
        await ExecAsync(connection, script);

        await ExecAsync(connection,
            "INSERT INTO `risk_acceptances` (`id`,`name`,`authorizing_manager_id`,`expires_at`,`created_at`) VALUES " +
            "(9901,'independent one',990,'2027-02-01','2026-02-01')," +
            "(9902,'independent two',990,'2027-03-01','2026-03-01');");

        var orphan = await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(connection,
            "INSERT INTO `risk_acceptances` (`id`,`name`,`authorizing_manager_id`,`expires_at`,`created_at`,`renewed_from_id`) " +
            "VALUES (9903,'invalid renewal',990,'2027-04-01','2026-04-01',9999);"));
        Assert.Equal(1452, orphan.Number);

        await using var index = new MySqlCommand("""
            SELECT NON_UNIQUE FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'risk_acceptances'
               AND INDEX_NAME = 'uq_ra_renewed_from_id'
            """, connection);
        var uniqueIndex = await index.ExecuteScalarAsync();
        Assert.NotNull(uniqueIndex);
        Assert.Equal(0L, Convert.ToInt64(uniqueIndex));

        await using var oldIndex = new MySqlCommand("""
            SELECT COUNT(*) FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'risk_acceptances'
               AND INDEX_NAME = 'idx_ra_renewed_from_id'
            """, connection);
        Assert.Equal(0L, Convert.ToInt64(await oldIndex.ExecuteScalarAsync()));

        await using var foreignKey = new MySqlCommand("""
            SELECT COUNT(*) FROM information_schema.KEY_COLUMN_USAGE
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'risk_acceptances'
               AND CONSTRAINT_NAME = 'fk_ra_renewed_from_id'
               AND COLUMN_NAME = 'renewed_from_id'
               AND REFERENCED_TABLE_NAME = 'risk_acceptances' AND REFERENCED_COLUMN_NAME = 'id'
            """, connection);
        Assert.Equal(1L, Convert.ToInt64(await foreignKey.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task EfMigrationUpAndDownKeepTheRenewalForeignKeySupported()
    {
        await fixture.InitializeNumberedSchemaAsync(0);
        await using var connection = await OpenAsync();
        await ExecAsync(connection, """
            CREATE TABLE `risk_acceptances` (
                `id` int NOT NULL,
                `renewed_from_id` int NULL,
                PRIMARY KEY (`id`),
                KEY `idx_ra_renewed_from_id` (`renewed_from_id`),
                CONSTRAINT `fk_ra_renewed_from_id` FOREIGN KEY (`renewed_from_id`)
                    REFERENCES `risk_acceptances` (`id`) ON DELETE RESTRICT
            );
            """);

        await using var context = fixture.NewContext();
        var migration = new ConcurrentRiskAcceptanceRenewal();
        var sqlGenerator = context.Database.GetService<IMigrationsSqlGenerator>();

        var upBuilder = new MigrationBuilder(context.Database.ProviderName!);
        typeof(ConcurrentRiskAcceptanceRenewal)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [upBuilder]);
        foreach (var command in sqlGenerator.Generate(upBuilder.Operations, context.Model))
            await ExecAsync(connection, command.CommandText);

        await using var upIndex = new MySqlCommand("""
            SELECT NON_UNIQUE FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'risk_acceptances'
               AND INDEX_NAME = 'uq_ra_renewed_from_id'
            """, connection);
        var uniqueIndex = await upIndex.ExecuteScalarAsync();
        Assert.NotNull(uniqueIndex);
        Assert.Equal(0L, Convert.ToInt64(uniqueIndex));

        var downBuilder = new MigrationBuilder(context.Database.ProviderName!);
        typeof(ConcurrentRiskAcceptanceRenewal)
            .GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [downBuilder]);
        foreach (var command in sqlGenerator.Generate(downBuilder.Operations, context.Model))
            await ExecAsync(connection, command.CommandText);

        await using var downIndex = new MySqlCommand("""
            SELECT NON_UNIQUE FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'risk_acceptances'
               AND INDEX_NAME = 'idx_ra_renewed_from_id'
            """, connection);
        var ordinaryIndex = await downIndex.ExecuteScalarAsync();
        Assert.NotNull(ordinaryIndex);
        Assert.Equal(1L, Convert.ToInt64(ordinaryIndex));

        await using var foreignKey = new MySqlCommand("""
            SELECT COUNT(*) FROM information_schema.KEY_COLUMN_USAGE
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'risk_acceptances'
               AND CONSTRAINT_NAME = 'fk_ra_renewed_from_id'
            """, connection);
        Assert.Equal(1L, Convert.ToInt64(await foreignKey.ExecuteScalarAsync()));
    }
}
