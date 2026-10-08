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
}
