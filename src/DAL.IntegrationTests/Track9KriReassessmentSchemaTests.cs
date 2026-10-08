using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySqlConnector;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.8 (S49 §5, §8 Q1–Q3) — the KRI and reassessment version on a real MariaDB: the five tables arrive with
/// nothing seeded and a retry converges; the CHECKs and unique indexes refuse what the service refuses — in particular
/// the database half of the idempotence (one event per opening reading, one trigger per event and risk), which the
/// in-memory provider of <c>MonitoringServiceInMemoryTest</c> cannot enforce; deleting a KRI or a risk removes what hangs
/// off it, deleting an incident only releases its event, and deleting a user only clears who wrote.
///
/// R1–R3 run the races the unique indexes settle through the real <see cref="MonitoringService"/>: the loser of the
/// episode race and the loser of the trigger race re-read and continue, and two evaluations of one KRI at once leave one
/// episode and one trigger per risk (S49 §4.7, D8).
///
/// The version is found by its marker, never written here (S42 §11, defect 1).
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9KriReassessmentSchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`risk_reassessment_triggers`");

    private static readonly string[] Tables =
        ["kris", "kri_readings", "kri_risks", "reassessment_events", "risk_reassessment_triggers"];

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

    private static async Task<MySqlException> RefusedAsync(MySqlConnection conn, string sql) =>
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, sql));

    /// <summary>Two users (980 writes, 981 opened the incident), two risks, an incident and a KRI (9801) with one reading (98011).</summary>
    private static Task SeedAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
        "VALUES (980,1,0,'local','U980','u980@x.test','s',REPEAT('x',60),1,0,'user980')," +
        "(981,1,0,'local','U981','u981@x.test','s',REPEAT('x',60),1,0,'user981');" +
        "INSERT INTO `risks` (`id`,`status`) VALUES (9811,'New'),(9812,'New');" +
        "INSERT INTO incidents (Id, Year, Sequence, Name, Description, Category, CreationDate, LastUpdate, CreatedById, Status) " +
        "VALUES (9821, 2026, 9821, '2026-9821', 'Ransomware.', 'malware', NOW(), NOW(), 981, 2);" +
        KriRow(9801) + ";" +
        "INSERT INTO kri_readings (id, kri_id, value, observed_at, created_at, recorded_by_id) VALUES (98011, 9801, 12, NOW(), NOW(), 980);");

    private static string KriRow(int id, int category = 1, int direction = 1, int maxAge = 31, string warning = "6") =>
        "INSERT INTO kris (id, name, category, source, unit, direction, tolerance_threshold, warning_threshold, " +
        "tolerance_rationale, max_reading_age_days, owner_id, created_at, updated_by_id) VALUES " +
        $"({id}, 'Hours down', {category}, 'Zabbix', 'hours', {direction}, 8, {warning}, 'Board minute', {maxAge}, 980, NOW(), 980)";

    private const string EventColumns =
        "(trigger_type, origin, title, occurred_at, incident_id, kri_id, kri_reading_id, kri_breach_ended_at, declared_by_id, created_at)";

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
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9KriReassessment'"));
    }

    /// <summary>Q2 — the CHECKs and unique indexes refuse what the service refuses, including a duplicate episode or trigger.</summary>
    [Fact]
    public async Task TestQ2_ConstraintsRefuseWhatTheServiceRefuses()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        // KRIs: an undefined category or direction, a reading age of 0, a warning beyond the tolerance.
        await RefusedAsync(conn, KriRow(9802, category: 5));
        await RefusedAsync(conn, KriRow(9803, direction: 3));
        await RefusedAsync(conn, KriRow(9804, maxAge: 0));
        await RefusedAsync(conn, KriRow(9805, warning: "9"));
        await RefusedAsync(conn, KriRow(9806, direction: 2, warning: "7"));

        // A voiding without its reason.
        await RefusedAsync(conn,
            "INSERT INTO kri_readings (kri_id, value, observed_at, created_at, voided_at) VALUES (9801, 3, NOW(), NOW(), NOW())");

        // A link twice.
        await ExecAsync(conn, "INSERT INTO kri_risks (kri_id, risk_id, created_at) VALUES (9801, 9811, NOW())");
        var link = await RefusedAsync(conn, "INSERT INTO kri_risks (kri_id, risk_id, created_at) VALUES (9801, 9811, NOW())");
        Assert.Contains("uq_kri_risks_kri_id_risk_id", link.Message);

        // Events: a KRI event without its reading, a declared one carrying a KRI, an incident on another type, type 7.
        await RefusedAsync(conn, $"INSERT INTO reassessment_events {EventColumns} VALUES (6, 2, 'x', NOW(), NULL, 9801, NULL, NULL, NULL, NOW())");
        await RefusedAsync(conn, $"INSERT INTO reassessment_events {EventColumns} VALUES (6, 1, 'x', NOW(), NULL, 9801, 98011, NULL, 980, NOW())");
        await RefusedAsync(conn, $"INSERT INTO reassessment_events {EventColumns} VALUES (4, 1, 'x', NOW(), 9821, NULL, NULL, NULL, 980, NOW())");
        await RefusedAsync(conn, $"INSERT INTO reassessment_events {EventColumns} VALUES (7, 1, 'x', NOW(), NULL, NULL, NULL, NULL, 980, NOW())");

        // The incident/type rule also holds on updates, rather than only at the insert path used by the service.
        await ExecAsync(conn, $"INSERT INTO reassessment_events {EventColumns} VALUES (3, 1, 'update', NOW(), 9821, NULL, NULL, NULL, 980, NOW())");
        await RefusedAsync(conn, "UPDATE reassessment_events SET trigger_type = 4 WHERE title = 'update'");
        await ExecAsync(conn, "DELETE FROM reassessment_events WHERE title = 'update'");

        // One episode per opening reading, one event per incident (S49 D8).
        await ExecAsync(conn, $"INSERT INTO reassessment_events {EventColumns} VALUES (6, 2, 'x', NOW(), NULL, 9801, 98011, NULL, NULL, NOW())");
        var episode = await RefusedAsync(conn,
            $"INSERT INTO reassessment_events {EventColumns} VALUES (6, 2, 'x', NOW(), NULL, 9801, 98011, NULL, NULL, NOW())");
        Assert.Contains("uq_reassessment_events_kri_reading_id", episode.Message);

        await ExecAsync(conn, $"INSERT INTO reassessment_events {EventColumns} VALUES (3, 1, 'x', NOW(), 9821, NULL, NULL, NULL, 980, NOW())");
        var incident = await RefusedAsync(conn,
            $"INSERT INTO reassessment_events {EventColumns} VALUES (3, 1, 'y', NOW(), 9821, NULL, NULL, NULL, 980, NOW())");
        Assert.Contains("uq_reassessment_events_incident_id", incident.Message);

        // Two declared events without an incident are fine: several NULLs in a unique index.
        await ExecAsync(conn, $"INSERT INTO reassessment_events {EventColumns} VALUES (1, 1, 'a', NOW(), NULL, NULL, NULL, NULL, 980, NOW())");
        await ExecAsync(conn, $"INSERT INTO reassessment_events {EventColumns} VALUES (1, 1, 'b', NOW(), NULL, NULL, NULL, NULL, 980, NOW())");

        // One trigger per event and risk.
        await ExecAsync(conn,
            "INSERT INTO risk_reassessment_triggers (event_id, risk_id, raised_at, created_at) " +
            "SELECT id, 9811, NOW(), NOW() FROM reassessment_events WHERE kri_reading_id = 98011");
        var trigger = await RefusedAsync(conn,
            "INSERT INTO risk_reassessment_triggers (event_id, risk_id, raised_at, created_at) " +
            "SELECT id, 9811, NOW(), NOW() FROM reassessment_events WHERE kri_reading_id = 98011");
        Assert.Contains("uq_risk_reassessment_triggers_event_id_risk_id", trigger.Message);
    }

    /// <summary>
    /// Q3 — deleting the user clears who wrote; deleting the incident releases its event; deleting a risk removes its links
    /// and triggers; deleting the KRI removes its readings, links and episodes.
    /// </summary>
    [Fact]
    public async Task TestQ3_DeletesCascadeAndAuthorsAreCleared()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        await ExecAsync(conn,
            "INSERT INTO kri_risks (kri_id, risk_id, created_at, created_by_id) VALUES (9801, 9811, NOW(), 980), (9801, 9812, NOW(), 980);" +
            $"INSERT INTO reassessment_events {EventColumns} VALUES (6, 2, 'kri', NOW(), NULL, 9801, 98011, NULL, NULL, NOW());" +
            $"INSERT INTO reassessment_events {EventColumns} VALUES (3, 1, 'incident', NOW(), 9821, NULL, NULL, NULL, 980, NOW());" +
            "INSERT INTO risk_reassessment_triggers (event_id, risk_id, raised_at, created_at) " +
            "SELECT id, 9811, NOW(), NOW() FROM reassessment_events;" +
            "INSERT INTO risk_reassessment_triggers (event_id, risk_id, raised_at, created_at) " +
            "SELECT id, 9812, NOW(), NOW() FROM reassessment_events WHERE origin = 2;");

        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 980");
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM kris WHERE owner_id IS NULL AND updated_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM kri_readings WHERE recorded_by_id IS NULL"));
        Assert.Equal(2L, await CountAsync(conn, "SELECT COUNT(*) FROM kri_risks WHERE created_by_id IS NULL"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM reassessment_events WHERE declared_by_id IS NOT NULL"));

        await ExecAsync(conn, "DELETE FROM incidents WHERE Id = 9821;");
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM reassessment_events WHERE title = 'incident' AND incident_id IS NULL"));

        // Incidents can also be removed by the CreatedBy user FK's ON DELETE CASCADE. MariaDB does not
        // activate an incident DELETE trigger for that path, so the reassessment FK itself must stay SET NULL.
        await ExecAsync(conn,
            "INSERT INTO incidents (Id, Year, Sequence, Name, Description, Category, CreationDate, LastUpdate, CreatedById, Status) " +
            "VALUES (9822, 2026, 9822, '2026-9822', 'Cascade.', 'malware', NOW(), NOW(), 981, 2);" +
            $"INSERT INTO reassessment_events {EventColumns} VALUES (3, 1, 'cascade-incident', NOW(), 9822, NULL, NULL, NULL, 981, NOW());" +
            "DELETE FROM `user` WHERE `value` = 981;");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM incidents WHERE Id = 9822"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM reassessment_events WHERE title = 'cascade-incident' AND incident_id IS NULL"));

        await ExecAsync(conn, "DELETE FROM risks WHERE id = 9812;");
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM kri_risks"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_reassessment_triggers WHERE risk_id = 9812"));

        await ExecAsync(conn, "DELETE FROM kris WHERE id = 9801;");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM kri_readings"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM kri_risks"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM reassessment_events WHERE origin = 2"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_reassessment_triggers"));
    }

    // --- R1–R3: the races, through the service ---------------------------------------------------------

    /// <summary>
    /// The concurrent writer of a race: just before the service's save of a <typeparamref name="T"/> reaches the database,
    /// another connection inserts the competing row, so the service's insert meets the real unique index.
    /// </summary>
    private sealed class CompetingWriter<T>(string connectionString, string sql) : SaveChangesInterceptor where T : class
    {
        public bool Fired { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
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
            if (!context.ChangeTracker.Entries<T>().Any(e => e.State == EntityState.Added)) return;

            Fired = true;
            using var conn = new MySqlConnection(connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }
    }

    private sealed class ContainerDal(MariaDbContainerFixture f, IInterceptor? racer = null) : IDalService
    {
        private readonly ServerVersion _version = ServerVersion.AutoDetect(f.ConnectionString);

        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false)
        {
            if (racer is null) return f.NewContext();

            var options = new DbContextOptionsBuilder<NRDbContext>()
                .UseMySql(f.ConnectionString + "ConvertZeroDateTime=True;", _version)
                .AddInterceptors(racer)
                .Options;
            return new AuditableContext(options);
        }

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private MonitoringService Service(IInterceptor? racer = null) =>
        new(Substitute.For<Serilog.ILogger>(), new ContainerDal(fixture, racer), Substitute.For<INotificationEventPublisher>());

    /// <summary>The latest schema, two open risks governed by KRI 9801, whose reading 98011 (12 h) is beyond its tolerance.</summary>
    private async Task<MySqlConnection> RaceFixtureAsync()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        var conn = await OpenAsync();
        await SeedAsync(conn);
        await ExecAsync(conn,
            "UPDATE risks SET subject = CONCAT('Risk ', id), reference_id = CONCAT('R-', id), assessment = '', notes = '', " +
            "submission_date = UTC_TIMESTAMP(), last_update = UTC_TIMESTAMP(), risk_catalog_mapping = '', " +
            "threat_catalog_mapping = '', template_group_id = 1 WHERE id IN (9811, 9812);" +
            "UPDATE kri_readings SET observed_at = UTC_TIMESTAMP() - INTERVAL 1 DAY WHERE id = 98011;" +
            "INSERT INTO kri_risks (kri_id, risk_id, created_at) VALUES (9801, 9811, UTC_TIMESTAMP()), (9801, 9812, UTC_TIMESTAMP());");
        return conn;
    }

    /// <summary>R1 — the evaluation that loses the episode race on the real index uses the winner's episode and triggers both risks.</summary>
    [Fact]
    public async Task TestR1_TheLoserOfTheEpisodeRaceUsesTheWinnersEpisode()
    {
        await using var conn = await RaceFixtureAsync();
        var racer = new CompetingWriter<ReassessmentEvent>(fixture.ConnectionString,
            $"INSERT INTO reassessment_events {EventColumns} VALUES (6, 2, 'winner', UTC_TIMESTAMP(), NULL, 9801, 98011, NULL, NULL, UTC_TIMESTAMP())");

        var summary = await Service(racer).EvaluateAllAsync();

        Assert.True(racer.Fired);
        Assert.Equal((0, 2), (summary.EpisodesOpened, summary.TriggersRaised));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM reassessment_events WHERE title = 'winner'"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM reassessment_events"));
        Assert.Equal(2L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_reassessment_triggers"));
    }

    /// <summary>
    /// R2 — the evaluation that loses the trigger race (a concurrent writer raised risk 9811 first) re-reads and still raises
    /// risk 9812: two triggers, each once.
    /// </summary>
    [Fact]
    public async Task TestR2_TheLoserOfTheTriggerRaceReReadsAndContinues()
    {
        await using var conn = await RaceFixtureAsync();
        await ExecAsync(conn,
            $"INSERT INTO reassessment_events {EventColumns} VALUES (6, 2, 'episode', UTC_TIMESTAMP(), NULL, 9801, 98011, NULL, NULL, UTC_TIMESTAMP())");
        var racer = new CompetingWriter<RiskReassessmentTrigger>(fixture.ConnectionString,
            "INSERT INTO risk_reassessment_triggers (event_id, risk_id, raised_at, created_at) " +
            "SELECT id, 9811, UTC_TIMESTAMP(), UTC_TIMESTAMP() FROM reassessment_events WHERE kri_reading_id = 98011");

        var summary = await Service(racer).EvaluateAllAsync();

        Assert.True(racer.Fired);
        Assert.Equal(1, summary.TriggersRaised);
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_reassessment_triggers WHERE risk_id = 9811"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_reassessment_triggers WHERE risk_id = 9812"));
    }

    /// <summary>
    /// R3 — two evaluations of the same KRI at the same time (the 06:45 job and a reading recorded that minute), each on
    /// its own connection, repeated: whatever the interleaving, one episode and one trigger per risk.
    /// </summary>
    [Fact]
    public async Task TestR3_TwoConcurrentEvaluationsLeaveOneEpisodeAndOneTriggerPerRisk()
    {
        await using var conn = await RaceFixtureAsync();

        for (var round = 0; round < 5; round++)
            await Task.WhenAll(Service().EvaluateAllAsync(), Service().EvaluateAllAsync());

        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM reassessment_events WHERE kri_id = 9801"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_reassessment_triggers WHERE risk_id = 9811"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_reassessment_triggers WHERE risk_id = 9812"));
    }
}
