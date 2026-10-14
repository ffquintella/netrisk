using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using ServerServices.Attribution;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// The M52 data version on a real MariaDB (S39 D-02, D-04, D-05; T238, T239, T240): findings the desktop
/// closed while the lifecycle stayed Active get the lifecycle state their workflow status means, with a
/// history row each; risks linked only through <c>risk_to_entity</c> get a scope by the documented rule,
/// with an audit row each; the seeded Very High becomes reachable unless an operator changed it; and a
/// second application changes nothing. The scenario-attribution union is checked here too, because the
/// in-memory provider cannot show that it is a real SQL <c>UNION</c>.
///
/// The version is found by the marker in its Data script — its Structure script is empty — never written
/// here (S42 §11, defect 1). Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class M52FoundationBackfillTests(MariaDbContainerFixture fixture)
{
    private static int V
    {
        get
        {
            var matches = Directory.GetFiles(Path.Combine(MariaDbContainerFixture.RepoDbDir(), "Data"), "*.sql")
                .Where(path => File.ReadAllText(path).Contains("T239 backfill:", StringComparison.Ordinal))
                .Select(path => int.Parse(Path.GetFileNameWithoutExtension(path), CultureInfo.InvariantCulture))
                .ToList();

            return matches.Count == 1
                ? matches[0]
                : throw new InvalidOperationException($"Expected one Data script with the T239 marker, found {matches.Count}.");
        }
    }

    // Entities: two units, a sub-unit, the organization and a process.
    private const int Unit = 9001;
    private const int SubUnit = 9002;
    private const int Organization = 9003;
    private const int Process = 9004;
    private const int OtherUnit = 9005;

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await ExecAsync(conn, "SET SESSION sql_mode = '';");
        return conn;
    }

    private static Task ExecAsync(MySqlConnection conn, string sql) => MariaDbContainerFixture.ExecAsync(conn, sql);

    private static async Task<object?> ScalarAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task<long> CountAsync(MySqlConnection conn, string sql) =>
        Convert.ToInt64(await ScalarAsync(conn, sql), CultureInfo.InvariantCulture);

    private static async Task<int?> IntAsync(MySqlConnection conn, string sql)
    {
        var value = await ScalarAsync(conn, sql);
        return value == null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static Task ApplyDataAsync(MySqlConnection conn, int version) =>
        ExecAsync(conn, File.ReadAllText(Path.Combine(MariaDbContainerFixture.RepoDbDir(), "Data", $"{version}.sql")));

    private static async Task ApplyVersionAsync(MySqlConnection conn, int version)
    {
        var structure = File.ReadAllText(Path.Combine(MariaDbContainerFixture.RepoDbDir(), "Structure", $"{version}.sql"));
        if (!string.IsNullOrWhiteSpace(structure)) await ExecAsync(conn, structure);
        await ApplyDataAsync(conn, version);
    }

    private static Task SeedEntitiesAsync(MySqlConnection conn) => ExecAsync(conn,
        "SET FOREIGN_KEY_CHECKS = 0;" +
        "INSERT INTO entities (Id, DefinitionName, DefinitionVersion, Created, Updated, CreatedBy, UpdatedBy, Status, Parent) VALUES " +
        $"({Unit},'organizationUnit','2.5',NOW(),NOW(),1,1,'active',{Organization})," +
        $"({SubUnit},'subOrganizationUnit','2.5',NOW(),NOW(),1,1,'active',{Unit})," +
        $"({Organization},'organization','2.5',NOW(),NOW(),1,1,'active',NULL)," +
        $"({Process},'businessProcess','2.5',NOW(),NOW(),1,1,'active',NULL)," +
        $"({OtherUnit},'organizationUnit','2.5',NOW(),NOW(),1,1,'active',{Organization});" +
        "SET FOREIGN_KEY_CHECKS = 1;");

    /// <summary>
    /// T239: every legacy-closed finding whose lifecycle is still Active moves to the state its workflow
    /// status means and gets one history row; nothing else moves.
    /// </summary>
    [Fact]
    public async Task TestLegacyClosedFindingsGetTheirLifecycleStateAndAHistoryRow()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();

        // (id, legacy Status, status_id). 107 is open; 108 is already FalsePositive; 109 was reopened
        // by a scanner regression; 110 has only its creation row.
        await ExecAsync(conn,
            "INSERT INTO `vulnerabilities` (`Id`,`Title`,`Status`,`status_id`) VALUES " +
            "(101,'fixed',25,1),(102,'rejected',11,1),(103,'not relevant',6,1),(104,'retired',27,1)," +
            "(105,'duplicated',12,1),(106,'outdated',29,1),(107,'new',1,1),(108,'closed but FP',4,3)," +
            "(109,'regressed',25,1),(110,'created',26,1);" +
            "INSERT INTO `finding_status_history` (`vulnerability_id`,`from_status_id`,`to_status_id`,`changed_at`,`source`) VALUES " +
            "(109,NULL,1,'2026-01-01',2),(109,7,1,'2026-03-01',2),(110,NULL,1,'2026-01-01',2);");

        await ApplyVersionAsync(conn, V);

        var expected = new Dictionary<int, int>
        {
            [101] = 7, [102] = 3, [103] = 4, [104] = 7, [105] = 5, [106] = 7, [107] = 1, [108] = 3, [109] = 1, [110] = 7
        };

        foreach (var (id, status) in expected)
            Assert.Equal(status, await IntAsync(conn, $"SELECT status_id FROM vulnerabilities WHERE Id = {id}"));

        foreach (var id in new[] { 101, 102, 103, 104, 105, 106, 110 })
        {
            Assert.Equal(1L, await CountAsync(conn,
                "SELECT COUNT(*) FROM finding_status_history h JOIN vulnerabilities v ON v.Id = h.vulnerability_id " +
                $"WHERE h.vulnerability_id = {id} AND h.from_status_id = 1 AND h.to_status_id = v.status_id " +
                "AND h.source = 3 AND h.user_id IS NULL AND h.justification LIKE 'T239 backfill:%'"));
        }

        foreach (var id in new[] { 107, 108, 109 })
            Assert.Equal(0L, await CountAsync(conn,
                $"SELECT COUNT(*) FROM finding_status_history WHERE vulnerability_id = {id} AND justification LIKE 'T239 backfill:%'"));

        Assert.Equal(V, await IntAsync(conn, "SELECT CAST(value AS UNSIGNED) FROM settings WHERE name = 'db_version'"));

        // A second application finds nothing left to move.
        await ApplyDataAsync(conn, V);
        Assert.Equal(7L, await CountAsync(conn,
            "SELECT COUNT(*) FROM finding_status_history WHERE justification LIKE 'T239 backfill:%'"));
    }

    /// <summary>
    /// T240: a risk without a scope takes the most specific organisational entity it is linked to, ties
    /// to the lowest id; identification links (a process) never become a scope; an explicit scope is
    /// never replaced; each change is audited once.
    /// </summary>
    [Fact]
    public async Task TestRisksTakeTheirScopeFromTheMostSpecificOrganisationalLink()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();
        await SeedEntitiesAsync(conn);

        // Foreign keys off for the seed only: the legacy risk row defaults submitted_by to a user this
        // test does not need. The upgrade itself runs with them on.
        await ExecAsync(conn,
            "SET FOREIGN_KEY_CHECKS = 0;" +
            "INSERT INTO `risks` (`id`,`status`,`subject`,`entity_id`) VALUES " +
            $"(1,'New','unit, sub-unit and process',NULL),(2,'New','two units',NULL),(3,'New','process only',NULL)," +
            $"(4,'New','explicit scope',{OtherUnit}),(5,'New','organization and unit',NULL),(6,'New','no link',NULL);" +
            "INSERT INTO `risk_to_entity` (`risk_id`,`entity_id`) VALUES " +
            $"(1,{Unit}),(1,{SubUnit}),(1,{Process}),(2,{OtherUnit}),(2,{Unit}),(3,{Process})," +
            $"(4,{Unit}),(5,{Organization}),(5,{OtherUnit});" +
            "SET FOREIGN_KEY_CHECKS = 1;");

        await ApplyVersionAsync(conn, V);

        var expected = new Dictionary<int, int?>
        {
            [1] = SubUnit, [2] = Unit, [3] = null, [4] = OtherUnit, [5] = OtherUnit, [6] = null
        };

        foreach (var (id, scope) in expected)
            Assert.Equal(scope, await IntAsync(conn, $"SELECT entity_id FROM risks WHERE id = {id}"));

        foreach (var id in new[] { 1, 2, 5 })
        {
            Assert.Equal(1L, await CountAsync(conn,
                "SELECT COUNT(*) FROM audit_logs a JOIN risks r ON r.id = a.entity_id " +
                $"WHERE a.entity_type = 'Risk' AND a.entity_id = {id} AND a.field = 'EntityId' AND a.old_value IS NULL " +
                "AND CAST(a.new_value AS SIGNED) = r.entity_id AND a.action = 2 AND a.actor = 'schema-upgrade' " +
                "AND a.correlation_id = 'T240-risk-scope-backfill'"));
        }

        Assert.Equal(3L, await CountAsync(conn,
            "SELECT COUNT(*) FROM audit_logs WHERE correlation_id = 'T240-risk-scope-backfill'"));

        await ApplyDataAsync(conn, V);
        Assert.Equal(3L, await CountAsync(conn,
            "SELECT COUNT(*) FROM audit_logs WHERE correlation_id = 'T240-risk-scope-backfill'"));
    }

    /// <summary>T238 leftover: the seeded 10.1 becomes 9.0, reachable by the 0–10 score.</summary>
    [Fact]
    public async Task TestTheSeededVeryHighBecomesReachable()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();

        Assert.Equal(10.1m, Convert.ToDecimal(await ScalarAsync(conn,
            "SELECT value FROM risk_levels WHERE name = 'Very High'"), CultureInfo.InvariantCulture));

        await ApplyVersionAsync(conn, V);

        Assert.Equal(9.0m, Convert.ToDecimal(await ScalarAsync(conn,
            "SELECT value FROM risk_levels WHERE name = 'Very High'"), CultureInfo.InvariantCulture));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_levels WHERE name = 'Very High'"));
    }

    /// <summary>An operator who already moved Very High keeps their value.</summary>
    [Fact]
    public async Task TestACustomisedVeryHighIsLeftAlone()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();
        await ExecAsync(conn, "UPDATE risk_levels SET value = 9.5 WHERE name = 'Very High';");

        await ApplyVersionAsync(conn, V);

        Assert.Equal(9.5m, Convert.ToDecimal(await ScalarAsync(conn,
            "SELECT value FROM risk_levels WHERE name = 'Very High'"), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The attribution union runs on MariaDB as a deduplicating UNION: a risk whose scope entity is also
    /// in its link table appears once for that entity, not twice.
    /// </summary>
    [Fact]
    public async Task TestScenarioAttributionIsADeduplicatedUnionOnMariaDb()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        await using (var conn = await OpenAsync())
        {
            await SeedEntitiesAsync(conn);
            await ExecAsync(conn,
                "SET FOREIGN_KEY_CHECKS = 0;" +
                "INSERT INTO `risks` (`id`,`status`,`subject`,`entity_id`) VALUES " +
                $"(1,'New','scope and links',{Unit}),(2,'New','links only',NULL),(3,'New','nothing',NULL);" +
                "INSERT INTO `risk_to_entity` (`risk_id`,`entity_id`) VALUES " +
                $"(1,{Unit}),(1,{Process}),(2,{SubUnit});" +
                "SET FOREIGN_KEY_CHECKS = 1;");
        }

        await using var db = fixture.NewContext();

        var pairs = await ScenarioAttribution.Pairs(db).ToListAsync();

        Assert.Equal(
            new[] { (1, Unit), (1, Process), (2, SubUnit) }.OrderBy(p => p.Item1).ThenBy(p => p.Item2),
            pairs.Select(p => (p.RiskId, p.EntityId)).OrderBy(p => p.Item1).ThenBy(p => p.Item2));

        var inUnit = await ScenarioAttribution.RiskIdsAttributedTo(db, [Unit, Process]).ToListAsync();
        Assert.Equal(new[] { 1 }, inUnit);
    }
}
