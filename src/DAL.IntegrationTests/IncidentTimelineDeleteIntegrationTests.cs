using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using MySqlConnector;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// S55 T354 review: <c>fk_inc_actions</c> (Structure/39.sql) has no ON DELETE, so MariaDB restricts deleting an incident
/// that a timeline entry points at. The EF in-memory store does not enforce foreign keys; this does, on the real schema.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class IncidentTimelineDeleteIntegrationTests(MariaDbContainerFixture fixture)
{
    private const int IncidentId = 4101;
    private static readonly User Analyst = new() { Value = 4102, Name = "Analyst", Type = "local", Lang = "en" };

    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) => f.NewContext();

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private IIncidentsService NewService() => new IncidentsService(Substitute.For<Serilog.ILogger>(), new ContainerDal(fixture),
        Substitute.For<IFilesService>(), Substitute.For<IIncidentResponsePlansService>(),
        Substitute.For<IIrpAutomationService>(), Substitute.For<INotificationEventPublisher>());

    private async Task<MySqlConnection> SeedAsync()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = ''; SET FOREIGN_KEY_CHECKS = 0;" +
            $"INSERT INTO `user` (`value`, `name`, `type`, `lang`) VALUES ({Analyst.Value}, 'Analyst', 'local', 'en');" +
            "INSERT INTO incidents (Id, Year, Sequence, Name, Description, Category, CreationDate, LastUpdate, CreatedById, Status) " +
            $"VALUES ({IncidentId}, 2026, 1, 'SI-2026-0001', 'd', 'not_specified', NOW(), NOW(), {Analyst.Value}, 1);" +
            "SET FOREIGN_KEY_CHECKS = 1;");
        return conn;
    }

    private static async Task<long> CountAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task TestTheForeignKeyRestrictsDeletingAnIncidentAnEntryPointsAt()
    {
        // The reason for the fix: with the constraint as shipped, a plain delete under a timeline row is refused.
        await using var conn = await SeedAsync();
        await NewService().AddTimelineEntryAsync(IncidentId, "Contained", Analyst);

        await Assert.ThrowsAsync<MySqlException>(() =>
            MariaDbContainerFixture.ExecAsync(conn, $"DELETE FROM incidents WHERE Id = {IncidentId};"));
    }

    [Fact]
    public async Task TestAnIncidentWithTimelineEntriesIsDeletedWithThem()
    {
        await using var conn = await SeedAsync();
        var service = NewService();
        await service.AddTimelineEntryAsync(IncidentId, "Contained", Analyst);
        await service.AddTimelineEntryAsync(IncidentId, "Eradicated", Analyst);

        await service.DeleteByIdAsync(IncidentId);

        Assert.Equal(0L, await CountAsync(conn, $"SELECT COUNT(*) FROM incidents WHERE Id = {IncidentId}"));
        Assert.Equal(0L, await CountAsync(conn, $"SELECT COUNT(*) FROM nr_actions WHERE IncidentId = {IncidentId}"));
        Assert.Equal(0L, await CountAsync(conn,
            "SELECT COUNT(*) FROM nr_actions WHERE ObjectType = 'Incident' AND Message IN ('Contained', 'Eradicated')"));
    }
}
