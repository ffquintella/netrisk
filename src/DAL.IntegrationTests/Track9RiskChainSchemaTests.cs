using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Model.Exceptions;
using Model.Risks.Chain;
using MySqlConnector;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.1 (S41 §5, §8) — <c>risk_chain_links</c> on a real MariaDB: the create-copy-coexist copy in
/// <c>Data/{n}.sql</c>, its replay, the race the unique index settles, the <c>CHECK</c>, and the
/// cascades.
///
/// The version number is read from <c>targetVersion</c>, never written here: the schema is built to
/// n − 1, seeded, and n applied, so the test survives the version being renumbered on merge (S41 R4).
/// The race and the CHECK are here because the in-memory provider enforces neither a unique index nor
/// a check constraint; <c>RiskChainServiceInMemoryTest</c> can only reach those branches with a double.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9RiskChainSchemaTests(MariaDbContainerFixture fixture)
{
    private static int N => MariaDbContainerFixture.TargetSchemaVersion;

    private const int Process = 10;
    private const int Unit = 100;
    private const int Data = 50;
    private const int App = 30;
    private const int Service = 20;
    private const int Objective = 1;
    private const int HostId = 1;

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

    private static async Task SeedEntityAsync(MySqlConnection conn, int id, string definition) =>
        await ExecAsync(conn,
            "INSERT INTO `entities` (`Id`,`DefinitionName`,`DefinitionVersion`,`CreatedBy`,`UpdatedBy`,`Status`) " +
            $"VALUES ({id},'{definition}','2.5',1,1,'active');");

    private static async Task SeedRiskAsync(MySqlConnection conn, int id, int? scopeEntityId = null) =>
        await ExecAsync(conn,
            "INSERT INTO `risks` (`id`,`status`,`subject`,`reference_id`,`assessment`,`notes`,`submission_date`," +
            "`last_update`,`risk_catalog_mapping`,`threat_catalog_mapping`,`template_group_id`,`entity_id`," +
            "`submitted_by`,`owner`,`manager`,`source`,`category`) " +
            $"VALUES ({id},'New','Risk {id}','R-{id}','','','2026-01-01 00:00:00','2026-01-01 00:00:00','','',1," +
            $"{(scopeEntityId?.ToString() ?? "NULL")},NULL,NULL,NULL,NULL,NULL);");

    // --- the copy -------------------------------------------------------------------------------

    /// <summary>
    /// Applying n copies exactly the chain-typed <c>risk_to_entity</c> rows, at the right level, as
    /// Legacy rows with no author. The unit is scope, not identification, and is left behind.
    /// </summary>
    [Fact]
    public async Task TestTheUpgradeCopiesOnlyChainTypedLegacyRows()
    {
        await fixture.InitializeNumberedSchemaAsync(N - 1);
        await using var conn = await OpenAsync();

        await SeedEntityAsync(conn, Process, "businessProcess");
        await SeedEntityAsync(conn, Unit, "organizationUnit");
        await SeedEntityAsync(conn, Data, "organizationData");
        await SeedEntityAsync(conn, App, "application");
        await SeedEntityAsync(conn, Service, "itService");
        for (var r = 1; r <= 5; r++) await SeedRiskAsync(conn, r);

        await ExecAsync(conn, "INSERT INTO `risk_to_entity` (`risk_id`,`entity_id`) VALUES " +
                              $"(1,{Process}),(1,{Unit}),(2,{Data}),(3,{App}),(4,{Service}),(5,{Unit});");

        await ApplyVersionAsync(conn, N);

        Assert.Equal(N.ToString(), (await CountAsync(conn,
            "SELECT value FROM settings WHERE name = 'db_version'")).ToString());

        var rows = new System.Collections.Generic.List<(int Risk, int Entity, int Level, int Origin, bool NoAuthor, bool NoHost)>();
        await using (var cmd = new MySqlCommand(
                         "SELECT risk_id, entity_id, chain_level, origin, created_by_id IS NULL, host_id IS NULL " +
                         "FROM risk_chain_links ORDER BY risk_id, entity_id", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                    reader.GetBoolean(4), reader.GetBoolean(5)));
        }

        var expected = new[]
        {
            (1, Process, (int)RiskChainLevel.Process),
            (2, Data, (int)RiskChainLevel.Data),
            (3, App, (int)RiskChainLevel.Asset),
            (4, Service, (int)RiskChainLevel.ItService)
        };
        Assert.Equal(expected, rows.Select(r => (r.Risk, r.Entity, r.Level)).ToArray());

        Assert.All(rows, r =>
        {
            Assert.Equal((int)RiskChainLinkOrigin.Legacy, r.Origin);
            Assert.True(r.NoAuthor);
            Assert.True(r.NoHost);
        });

        // risks.entity_id — the scope column — was not touched.
        Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM risks WHERE entity_id IS NOT NULL"));
    }

    /// <summary>Re-running the Data script — a retry after a failure — adds no duplicate, and running
    /// the Structure script again changes nothing.</summary>
    [Fact]
    public async Task TestReplayingTheVersionAddsNoDuplicate()
    {
        await fixture.InitializeNumberedSchemaAsync(N - 1);
        await using var conn = await OpenAsync();

        await SeedEntityAsync(conn, Process, "businessProcess");
        await SeedEntityAsync(conn, Objective, "strategicObjective");
        await SeedRiskAsync(conn, 1);
        await ExecAsync(conn, $"INSERT INTO `risk_to_entity` (`risk_id`,`entity_id`) VALUES (1,{Process}),(1,{Objective});");

        await ApplyVersionAsync(conn, N);
        Assert.Equal(2, await CountAsync(conn, "SELECT COUNT(*) FROM risk_chain_links"));

        await ExecAsync(conn, $"UPDATE settings SET value = '{N - 1}' WHERE name = 'db_version';");
        await ApplyVersionAsync(conn, N, "Structure");
        await ApplyVersionAsync(conn, N, "Data");

        Assert.Equal(2, await CountAsync(conn, "SELECT COUNT(*) FROM risk_chain_links"));
        Assert.Equal(1, await CountAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9RiskChainLinks'"));
    }

    // --- the race on the unique index -------------------------------------------------------------

    /// <summary>
    /// The concurrent writer of a race: just before the service's save reaches the database — after
    /// its existence check has already passed — another connection inserts the same (risk, entity)
    /// link. The service's insert then meets the real unique index, which is the situation S41 §5.4
    /// describes and the in-memory suite cannot produce.
    /// </summary>
    private sealed class CompetingWriter(string connectionString, string sql) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            Fire(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Fire(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Fire(DbContext? context)
        {
            if (Fired || context is null) return;
            if (!context.ChangeTracker.Entries<RiskChainLink>().Any(e => e.State == EntityState.Added)) return;

            Fired = true;
            using var conn = new MySqlConnection(connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }
    }

    private sealed class RacingDal(MariaDbContainerFixture f, IInterceptor racer) : IDalService
    {
        private readonly ServerVersion _version = ServerVersion.AutoDetect(f.ConnectionString);

        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false)
        {
            var options = new DbContextOptionsBuilder<NRDbContext>()
                .UseMySql(f.ConnectionString + "ConvertZeroDateTime=True;", _version)
                .AddInterceptors(racer)
                .Options;
            return new AuditableContext(options);
        }

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private async Task<MySqlConnection> RaceFixtureAsync()
    {
        await fixture.InitializeNumberedSchemaAsync(N);
        var conn = await OpenAsync();
        await SeedEntityAsync(conn, Process, "businessProcess");
        await SeedRiskAsync(conn, 1);
        return conn;
    }

    private static string CompetingInsert(int origin) =>
        "INSERT INTO risk_chain_links (risk_id, chain_level, entity_id, origin, created_at) " +
        $"VALUES (1, 2, {Process}, {origin}, UTC_TIMESTAMP());";

    [Fact]
    public async Task TestThePostThatLosesTheRaceIsAConflict()
    {
        await using var conn = await RaceFixtureAsync();
        var racer = new CompetingWriter(fixture.ConnectionString, CompetingInsert((int)RiskChainLinkOrigin.Legacy));
        var service = new RiskChainService(Substitute.For<Serilog.ILogger>(), new RacingDal(fixture, racer));

        var admin = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")], "test"));

        await Assert.ThrowsAsync<DataAlreadyExistsException>(() =>
            service.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Process }, null, admin));

        Assert.True(racer.Fired);
        Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM risk_chain_links"));
    }

    [Fact]
    public async Task TestTheMirrorThatLosesTheRaceStillSavesTheLegacyRow()
    {
        await using var conn = await RaceFixtureAsync();
        var racer = new CompetingWriter(fixture.ConnectionString, CompetingInsert((int)RiskChainLinkOrigin.Declared));
        var risks = new RisksService(new RacingDal(fixture, racer), Substitute.For<IRolesService>(),
            Substitute.For<ServerServices.Filtering.IEntityFilterMapperProvider>(), Substitute.For<IUsersService>(),
            Substitute.For<INotificationEventPublisher>(), Substitute.For<IRiskWorkflowService>());

        risks.AssociateRiskWithEntity(1, Process);

        Assert.True(racer.Fired);
        Assert.Equal(1, await CountAsync(conn, $"SELECT COUNT(*) FROM risk_to_entity WHERE risk_id = 1 AND entity_id = {Process}"));
        Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM risk_chain_links"));
        Assert.Equal((int)RiskChainLinkOrigin.Declared, (int)await CountAsync(conn, "SELECT origin FROM risk_chain_links"));
    }

    // --- the constraints --------------------------------------------------------------------------

    [Fact]
    public async Task TestTheCheckRefusesBothTargetsAndNoTarget()
    {
        await fixture.InitializeNumberedSchemaAsync(N);
        await using var conn = await OpenAsync();
        await SeedEntityAsync(conn, Process, "businessProcess");
        await SeedRiskAsync(conn, 1);
        await ExecAsync(conn, $"INSERT INTO hosts (Id, HostName, Source) VALUES ({HostId}, 'h1', 'test');");

        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn,
            "INSERT INTO risk_chain_links (risk_id, chain_level, entity_id, host_id, origin, created_at) " +
            $"VALUES (1, 5, {Process}, {HostId}, 1, UTC_TIMESTAMP());"));

        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn,
            "INSERT INTO risk_chain_links (risk_id, chain_level, entity_id, host_id, origin, created_at) " +
            "VALUES (1, 5, NULL, NULL, 1, UTC_TIMESTAMP());"));

        Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM risk_chain_links"));
    }

    [Fact]
    public async Task TestTheUniqueIndexesRefuseADuplicateButNotAHostBesideAnEntity()
    {
        await fixture.InitializeNumberedSchemaAsync(N);
        await using var conn = await OpenAsync();
        await SeedEntityAsync(conn, Process, "businessProcess");
        await SeedRiskAsync(conn, 1);
        await ExecAsync(conn, $"INSERT INTO hosts (Id, HostName, Source) VALUES ({HostId}, 'h1', 'test'), (2, 'h2', 'test');");

        await ExecAsync(conn, CompetingInsert(1));
        var duplicate = await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, CompetingInsert(2)));
        Assert.Contains("uq_risk_chain_links_risk_id_entity_id", duplicate.Message);

        // Two host links and an entity link on the same risk: the NULLs do not collide.
        await ExecAsync(conn, "INSERT INTO risk_chain_links (risk_id, chain_level, host_id, origin, created_at) " +
                              $"VALUES (1, 5, {HostId}, 1, UTC_TIMESTAMP()), (1, 5, 2, 1, UTC_TIMESTAMP());");
        var hostDuplicate = await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn,
            "INSERT INTO risk_chain_links (risk_id, chain_level, host_id, origin, created_at) " +
            $"VALUES (1, 5, {HostId}, 1, UTC_TIMESTAMP());"));
        Assert.Contains("uq_risk_chain_links_risk_id_host_id", hostDuplicate.Message);

        Assert.Equal(3, await CountAsync(conn, "SELECT COUNT(*) FROM risk_chain_links"));
    }

    [Fact]
    public async Task TestDeletingTheEntityTheHostOrTheRiskRemovesItsLinks()
    {
        await fixture.InitializeNumberedSchemaAsync(N);
        await using var conn = await OpenAsync();
        await SeedEntityAsync(conn, Process, "businessProcess");
        await SeedEntityAsync(conn, App, "application");
        await SeedRiskAsync(conn, 1);
        await SeedRiskAsync(conn, 2);
        await ExecAsync(conn, $"INSERT INTO hosts (Id, HostName, Source) VALUES ({HostId}, 'h1', 'test');");

        await ExecAsync(conn,
            "INSERT INTO risk_chain_links (risk_id, chain_level, entity_id, host_id, origin, created_at) VALUES " +
            $"(1, 2, {Process}, NULL, 1, UTC_TIMESTAMP()), (1, 5, NULL, {HostId}, 1, UTC_TIMESTAMP()), " +
            $"(2, 5, {App}, NULL, 1, UTC_TIMESTAMP());");

        await ExecAsync(conn, $"DELETE FROM entities WHERE Id = {Process};");
        Assert.Equal(0, await CountAsync(conn, $"SELECT COUNT(*) FROM risk_chain_links WHERE entity_id = {Process}"));

        await ExecAsync(conn, $"DELETE FROM hosts WHERE Id = {HostId};");
        Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM risk_chain_links WHERE host_id IS NOT NULL"));

        await ExecAsync(conn, "DELETE FROM risks WHERE id = 2;");
        Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM risk_chain_links"));
    }
}
