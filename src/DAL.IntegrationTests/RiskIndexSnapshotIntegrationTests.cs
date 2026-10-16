using System.Diagnostics;
using System.Globalization;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Model.RiskIndex;
using MySqlConnector;
using NSubstitute;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using ServerServices.Security;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// M53, T247 on a real MariaDB (S40 §9.3, §12.7): version 107 adds the snapshot job's tables and columns and converges on
/// a replay; its CHECKs hold; the job writes a day in one transaction through the production <see cref="DalService"/> —
/// whose retrying execution strategy refuses a bare transaction — so a failure part-way leaves nothing; the new scope
/// filters translate; and 35 000 object rows go in within the budget. The version is found by its marker. Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class RiskIndexSnapshotIntegrationTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("CREATE TABLE IF NOT EXISTS `risk_index_node_members`");

    private static readonly DateTime Now = new(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly D = DateOnly.FromDateTime(Now);
    private const int Unit = 9101, Other = 9102, Host = 9201, ProfileId = 9001;

    private static readonly string[] Tables =
        ["risk_index_object_attributions", "risk_index_node_members", "risk_index_class_days", "risk_index_incident_closures"];

    private static readonly (string Table, string Column)[] Columns =
    [
        ("risk_index_snapshots", "display_state"), ("risk_index_snapshots", "category_state"),
        ("risk_index_snapshots", "markers"), ("risk_index_snapshots", "chips"),
        ("risk_index_object_days", "freshness"), ("risk_index_object_days", "coverage_effect"),
        ("risk_index_object_days", "r_max"), ("risk_index_object_days", "cleared_r")
    ];

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task<long> CountAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static Task ExecAsync(MySqlConnection conn, string sql) => MariaDbContainerFixture.ExecAsync(conn, sql);

    /// <summary>The production data access, under the job host's unrestricted principal (T237).</summary>
    private DalService ProductionDal() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = fixture.ConnectionString + "ConvertZeroDateTime=True;"
        }).Build(),
        BackgroundServiceHttpContextAccessor.WithUnrestrictedEntityScope());

    private static RiskIndexSnapshotService Service(IDalService dal) =>
        new(dal, new RiskIndexDayCalculator(dal), Substitute.For<Serilog.ILogger>());

    private const string ProfileSql =
        "INSERT INTO risk_index_profiles (id, version, name, mode, status, parameters, justification, submitted_at, " +
        "approved_at, committee_decision_ref, effective_from, created_at) VALUES ({0}, 1, 'Pilot', 1, 3, '{1}', 'pilot', " +
        "'2026-08-25', '2026-08-31', 'CRTI-2026-09', '2026-09-01 05:00:00', '2026-08-20')";

    /// <summary>A unit with a server and a finding, another unit with a risk, and a profile in force since the pilot.</summary>
    private async Task SeedAsync()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);

        await using (var db = fixture.NewContext())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0; SET SESSION sql_mode = '';");
            foreach (var id in new[] { Unit, Other })
                db.Entities.Add(new Entity
                {
                    Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "2.7", Status = "active",
                    Created = Now.AddYears(-1), Updated = Now.AddYears(-1), CreatedBy = 1, UpdatedBy = 1
                });
            db.Hosts.Add(new Host
            {
                Id = Host, HostName = "srv-01", Source = "test", Status = 1, EntityId = Unit, Criticality = 4,
                RegistrationDate = Now.AddDays(-100)
            });
            db.Vulnerabilities.Add(new Vulnerability
            {
                Id = 990001, HostId = Host, ImportSource = "nessus", Cvss3BaseScore = 8.1f, Cves = "CVE-2026-0101",
                LifecycleStatus = FindingStatus.Active, FirstDetection = Now.AddDays(-30), LastDetection = Now.AddDays(-1),
                Title = "finding", Status = 1, DetectionCount = 1
            });
            db.Risks.Add(new Risk
            {
                Id = 9301, EntityId = Other, Status = "New", Subject = "Leak", ReferenceId = "R-9301", Assessment = "", Notes = "",
                RiskCatalogMapping = "", ThreatCatalogMapping = "", SubmissionDate = new DateTime(2026, 1, 10),
                LastUpdate = new DateTime(2026, 1, 10), Owner = 1, SubmittedBy = 1
            });
            db.RiskScorings.Add(new RiskScoring { Id = 9301, CalculatedRisk = 7f, ResidualRisk = 6f, ClassicImpact = 4, ScoringMethod = 1 });
            await db.SaveChangesAsync();
        }

        await using var conn = await OpenAsync();
        await ExecAsync(conn, string.Format(CultureInfo.InvariantCulture, ProfileSql, ProfileId,
            RiskIndexParametersJson.Serialize(new RiskIndexParameters()).Replace("'", "''")) + ";");
    }

    // --- the version ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestQ1_TheVersionAddsTheTablesAndColumnsAndConvergesOnAReplay()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();
        var dir = MariaDbContainerFixture.RepoDbDir();

        var structure = await File.ReadAllTextAsync(Path.Combine(dir, "Structure", $"{V}.sql"));
        await ExecAsync(conn, structure);
        await ExecAsync(conn, structure);
        await ExecAsync(conn, await File.ReadAllTextAsync(Path.Combine(dir, "Data", $"{V}.sql")));

        foreach (var table in Tables)
            Assert.Equal(1, await CountAsync(conn,
                $"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}'"));
        foreach (var (table, column) in Columns)
            Assert.Equal(1, await CountAsync(conn,
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                $"AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'"));
        foreach (var check in new[] { "ck_risk_index_snapshots_display_state", "ck_risk_index_snapshots_category_state", "ck_risk_index_object_days_engine" })
            Assert.Equal(1, await CountAsync(conn,
                $"SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = '{check}'"));

        Assert.Equal(V, await CountAsync(conn, "SELECT CAST(value AS UNSIGNED) FROM settings WHERE name = 'db_version'"));
        Assert.Equal(1, await CountAsync(conn,
            "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20261016162414_RiskIndexSnapshotJobTables'"));
    }

    [Fact]
    public async Task TestQ2_TheChecksHoldWhatTheRowsMean()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        await using var conn = await OpenAsync();
        await ExecAsync(conn, string.Format(CultureInfo.InvariantCulture, ProfileSql, ProfileId, "{}") + ";");

        string Snapshot(int category, string state) =>
            "INSERT INTO risk_index_snapshots (snapshot_date, profile_id, mode, scope_kind, scope_ref, category, value_unfloored, " +
            "value_floored, value_displayed, coverage, freshness, quality, object_count, presumption_points, external_inputs_hash, " +
            $"created_at, {state.Split('=')[0]}) VALUES ('2026-10-05', {ProfileId}, 1, 1, 'org', {category}, 1, 1, 1, 1, 1, 1, 1, 0, " +
            $"REPEAT('a', 64), NOW(), {state.Split('=')[1]});";

        await ExecAsync(conn, Snapshot(0, "display_state=1"));
        await ExecAsync(conn, Snapshot(2, "category_state=1"));
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, Snapshot(3, "display_state=1")));
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, Snapshot(4, "category_state=9")));

        await ExecAsync(conn, "INSERT INTO risk_index_node_members (snapshot_date, profile_id, node_entity_id, member_kind, " +
                              $"member_entity_id, created_at) VALUES ('2026-10-05', {ProfileId}, 10, 2, NULL, NOW());");
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn,
            "INSERT INTO risk_index_node_members (snapshot_date, profile_id, node_entity_id, member_kind, member_entity_id, " +
            $"created_at) VALUES ('2026-10-05', {ProfileId}, 10, 1, NULL, NOW());"));

        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn,
            "INSERT INTO risk_index_class_days (snapshot_date, profile_id, node_entity_id, tab_group, category, value, created_at) " +
            $"VALUES ('2026-10-05', {ProfileId}, 10, 1, 2, 101, NOW());"));
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn,
            "INSERT INTO risk_index_object_days (snapshot_date, profile_id, object_kind, object_ref, category, population, r_value, " +
            "weight, criticality, criticality_source, state, attribution, factors, freshness, created_at) " +
            $"VALUES ('2026-10-05', {ProfileId}, 2, 'h', 2, 2, 0.5, 1, 3, 'default', 1, '[]', '[]', 1.5, NOW());"));
    }

    // --- the job's write -----------------------------------------------------------------------------------------------

    /// <summary>
    /// S40 §9.3 step 5 and §12.6 "falha no meio → nenhuma linha": a class-day row already holding the key the job writes
    /// last makes the run fail after thousands of rows went in; none of them stays. Without the conflict the same run
    /// writes the day, through the production execution strategy, and a second run writes nothing.
    /// </summary>
    [Fact]
    public async Task TestQ3_TheDayIsOneTransactionThroughTheProductionDataAccess()
    {
        await SeedAsync();
        var dal = ProductionDal();
        await using (var conn = await OpenAsync())
            await ExecAsync(conn, "INSERT INTO risk_index_class_days (snapshot_date, profile_id, node_entity_id, tab_group, " +
                                  $"category, value, created_at) VALUES ('{D:yyyy-MM-dd}', {ProfileId}, {Unit}, 1, 2, 1, NOW());");

        var failed = await Service(dal).RunAsync(Now);

        Assert.Equal(RiskIndexSnapshotResult.Failed, failed.Result);
        await using (var conn = await OpenAsync())
        {
            Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM risk_index_snapshots"));
            Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM risk_index_object_days"));
            Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM risk_index_object_attributions"));
            Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM risk_index_node_members"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM risk_index_class_days"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM integration_sync_logs WHERE integration = 8 AND status = 4"));
            await ExecAsync(conn, "DELETE FROM risk_index_class_days;");
        }

        var written = await Service(dal).RunAsync(Now);
        var again = await Service(dal).RunAsync(Now.AddHours(2));

        Assert.Equal(RiskIndexSnapshotResult.Written, written.Result);
        Assert.Equal(RiskIndexSnapshotResult.AlreadyComputed, again.Result);
        await using (var conn = await OpenAsync())
        {
            Assert.Equal(written.Counts!.Snapshots, await CountAsync(conn, "SELECT COUNT(*) FROM risk_index_snapshots"));
            Assert.Equal(written.Counts.ObjectDays, await CountAsync(conn, "SELECT COUNT(*) FROM risk_index_object_days"));
            Assert.Equal(written.Counts.ClassDays, await CountAsync(conn, "SELECT COUNT(*) FROM risk_index_class_days"));
            Assert.Equal(1, await CountAsync(conn,
                $"SELECT COUNT(*) FROM risk_index_class_days WHERE node_entity_id = {Unit} AND tab_group = 1 AND category = 2"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM integration_sync_logs WHERE integration = 8 AND status = 2"));
        }

        // The next day reads this one back — the object rows, the closures and the chips — and attributes against it.
        var next = await Service(dal).RunAsync(Now.AddDays(1));
        Assert.Equal(RiskIndexSnapshotResult.Written, next.Result);
        await using var db = fixture.NewContext();
        var root = await db.RiskIndexSnapshots.AsNoTracking().SingleAsync(s => s.SnapshotDate == D.AddDays(1)
            && s.ScopeRef == NodeMembership.RootRef && s.Category == RiskIndexCategory.Icr);
        Assert.Equal(D, RiskIndexStoredJson.Deserialize<StoredChangeAttribution>(root.ChangeAttribution)!.ComparedWith);
    }

    /// <summary>The new filters translate to SQL: an attribution by its row's scope entity, a class day by its node.</summary>
    [Fact]
    public async Task TestQ4_AScopedContextSeesOnlyItsOwnAttributionsAndClassDays()
    {
        await SeedAsync();
        Assert.Equal(RiskIndexSnapshotResult.Written, (await Service(ProductionDal()).RunAsync(Now)).Result);

        await using (var scoped = fixture.NewScopedContext(Unit))
        {
            var attributions = await scoped.RiskIndexObjectAttributions.ToListAsync();
            Assert.NotEmpty(attributions);
            Assert.All(attributions, a => Assert.Equal(Unit, a.ScopeEntityId));
            var classDays = await scoped.RiskIndexClassDays.ToListAsync();
            Assert.NotEmpty(classDays);
            Assert.All(classDays, c => Assert.Equal(Unit, c.NodeEntityId));
        }

        await using var unrestricted = fixture.NewContext();
        Assert.Contains(await unrestricted.RiskIndexClassDays.ToListAsync(), c => c.NodeEntityId == null);
        Assert.Contains(await unrestricted.RiskIndexObjectAttributions.ToListAsync(), a => a.ScopeEntityId == Other);
    }

    /// <summary>S40 §12.7 <c>RiskIndexBulkInsertTests</c>: 35 000 object rows, with their attributions, in one transaction.</summary>
    [Fact]
    public async Task TestQ5_ThirtyFiveThousandObjectRowsGoInWithinTheBudget()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        await using (var conn = await OpenAsync())
            await ExecAsync(conn, string.Format(CultureInfo.InvariantCulture, ProfileSql, ProfileId, "{}") + ";");

        var rows = Enumerable.Range(1, 35_000).Select(i => new RiskIndexObjectRow
        {
            Key = new IcrObjectKey(RiskIndexObjectKind.Host, i.ToString(CultureInfo.InvariantCulture)),
            Category = RiskIndexCategory.Exp, Population = RiskIndexPopulation.Assets, R = (i % 100) / 100.0, Weight = 0.85,
            Criticality = 4, State = RiskIndexObjectState.Assessed, ScopeEntityId = Unit, Attribution = [Unit],
            AssetClass = RiskIndexAssetClass.Server,
            Factors = [new RiskIndexFactorReading { Factor = "EXP.D", Type = RiskIndexFactorType.V, X = 0.5, Source = "V1-DEV" }],
            Signals = [new RiskIndexSignalReading("EXP.V", $"CVE-2026-{i}", 0.7, ["kev"])]
        }).ToList();
        var root = new RiskIndexNodeDefinition { ScopeKind = RiskIndexScopeKind.Organization, ScopeRef = NodeMembership.RootRef };
        var day = new RiskIndexDay
        {
            Objects = rows,
            Nodes =
            [
                new RiskIndexNodeDay
                {
                    Node = root, Members = new NodeMembers { Node = root, Closure = new HashSet<int> { Unit } },
                    Result = new IcrNodeResult { DisplayState = IcrDisplayState.Point, Unfloored = 40, Floored = 40, Displayed = 40 }
                }
            ]
        };
        var profile = new RiskIndexProfile { Id = ProfileId, Version = 1, Name = "Pilot", Parameters = "{}" };
        var service = Service(ProductionDal());

        var clock = Stopwatch.StartNew();
        var counts = await service.WriteAsync(() => RiskIndexSnapshotService.BuildRows(day, profile, new RiskIndexParameters(),
                D, new string('A', 64), null, []), new Dictionary<int, DateTime>(), new Dictionary<int, DateTime>(), D, ProfileId,
            CancellationToken.None);
        clock.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{counts.ObjectDays} object rows and {counts.Attributions} attributions written in {clock.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(35_000, counts.ObjectDays);
        Assert.Equal(35_000, counts.Attributions);
        Assert.True(clock.Elapsed < TimeSpan.FromMinutes(5), $"The write took {clock.Elapsed}");
        await using var check = await OpenAsync();
        Assert.Equal(35_000, await CountAsync(check, "SELECT COUNT(*) FROM risk_index_object_days"));
    }
}
