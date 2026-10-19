using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// T292 and T241 on a real MariaDB (S40 §12.7). The in-memory provider evaluates any predicate it cannot translate on the
/// client, so it would pass even if the expanded scope never reached SQL; here the production <see cref="DalService"/>,
/// pointed at the container, derives the scope from a principal and every scoped table answers in SQL. And version 104
/// seeds the two permissions once, granted by role name, and converges when replayed. Needs Docker.
/// <code>
///   Org 1 ─┬─ Unit X 10 ── Sub X 11 ── Process 12
///          └─ Unit Y 20 ─┬─ Sub Y 21
///                        └─ Process P 30 (organizationUnit = X, EAV only)
/// </code>
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class EntityScopeHierarchyIntegrationTests(MariaDbContainerFixture fixture)
{
    private const int Org = 1, UnitX = 10, SubX = 11, ProcessUnderSubX = 12, UnitY = 20, SubY = 21, EavProcess = 30;
    private static readonly int[] AllEntities = [Org, UnitX, SubX, ProcessUnderSubX, UnitY, SubY, EavProcess];

    /// <summary>
    /// The version that seeds the permissions, found by its content rather than written here (S42 §11): the permission's
    /// own VALUES row, since a later version may grant it by key (T293's Data script does).
    /// </summary>
    private static int V =>
        Directory.GetFiles(Path.Combine(MariaDbContainerFixture.RepoDbDir(), "Data"), "*.sql")
            .Where(path => File.ReadAllText(path).Contains("('entity_risk_context',", StringComparison.Ordinal))
            .Select(path => int.Parse(Path.GetFileNameWithoutExtension(path)))
            .Single();

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task<long> CountAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static string Granted(string role, string key) =>
        "SELECT COUNT(*) FROM role_responsibilities rr JOIN `role` r ON r.`value` = rr.role_id " +
        $"JOIN permissions p ON p.id = rr.permission_id WHERE r.`name` = '{role}' AND p.`key` = '{key}'";

    [Fact]
    public async Task TestTheUpgradeSeedsBothPermissionsOnceGrantsThemByRoleNameAndConverges()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();

        var data = await File.ReadAllTextAsync(Path.Combine(MariaDbContainerFixture.RepoDbDir(), "Data", $"{V}.sql"));
        await MariaDbContainerFixture.ExecAsync(conn, data);
        await MariaDbContainerFixture.ExecAsync(conn, $"UPDATE settings SET value = '{V - 1}' WHERE name = 'db_version';");
        await MariaDbContainerFixture.ExecAsync(conn, data);

        Assert.Equal((long)V, await CountAsync(conn, "SELECT CAST(value AS UNSIGNED) FROM settings WHERE name = 'db_version'"));

        foreach (var key in new[] { "entities_manage", "entity_risk_context" })
        {
            Assert.Equal(1L, await CountAsync(conn, $"SELECT COUNT(*) FROM permissions WHERE `key` = '{key}'"));
            Assert.Equal(1L, await CountAsync(conn, Granted("Administrator", key)));
        }

        Assert.Equal(1L, await CountAsync(conn, Granted("RiskAnalyst", "entities_manage")));
        Assert.Equal(0L, await CountAsync(conn, Granted("RiskAnalyst", "entity_risk_context")));
        Assert.Equal(3L, await CountAsync(conn,
            "SELECT COUNT(*) FROM role_responsibilities rr JOIN permissions p ON p.id = rr.permission_id " +
            "WHERE p.`key` IN ('entities_manage', 'entity_risk_context')"));
    }

    private async Task SeedTreeAsync()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        await using var conn = await OpenAsync();

        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = ''; SET FOREIGN_KEY_CHECKS = 0;");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO entities (Id, DefinitionName, DefinitionVersion, Created, Updated, CreatedBy, UpdatedBy, Status, Parent) VALUES " +
            $"({Org},'organization','2.6',NOW(),NOW(),1,1,'active',NULL)," +
            $"({UnitX},'organizationUnit','2.6',NOW(),NOW(),1,1,'active',{Org})," +
            $"({SubX},'subOrganizationUnit','2.6',NOW(),NOW(),1,1,'active',{UnitX})," +
            $"({ProcessUnderSubX},'businessProcess','2.6',NOW(),NOW(),1,1,'active',{SubX})," +
            $"({UnitY},'organizationUnit','2.6',NOW(),NOW(),1,1,'active',{Org})," +
            $"({SubY},'subOrganizationUnit','2.6',NOW(),NOW(),1,1,'active',{UnitY})," +
            $"({EavProcess},'businessProcess','2.6',NOW(),NOW(),1,1,'active',{UnitY});" +
            "INSERT INTO entities_properties (Type, Value, OldValue, Entity, Name) " +
            $"VALUES ('organizationUnit','{UnitX}','',{EavProcess},'organizationUnit-{EavProcess}');");

        foreach (var id in AllEntities)
        {
            await MariaDbContainerFixture.ExecAsync(conn,
                $"INSERT INTO `risks` (`id`,`status`,`subject`,`entity_id`) VALUES ({id},'New','risk {id}',{id});" +
                $"INSERT INTO `vulnerabilities` (`id`,`title`,`status`,`EntityId`) VALUES ({id},'vuln {id}',1,{id});" +
                $"INSERT INTO `hosts` (`Id`,`HostName`,`Source`,`entity_id`) VALUES ({id},'h-{id}','test',{id});" +
                "INSERT INTO incidents (Id, Year, Sequence, Name, Description, Category, CreationDate, LastUpdate, CreatedById, Status, entity_id) " +
                $"VALUES ({id}, 2026, {id}, '2026-{id:0000}', 'i', 'c', NOW(), NOW(), 1, 2, {id});" +
                $"INSERT INTO `assessments` (`id`,`name`,`entity_id`) VALUES ({id},'assessment {id}',{id});");
        }

        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 1;");
    }

    private DalService DalActingAs(params Claim[] claims)
    {
        var connectionString = fixture.ConnectionString.Contains("ConvertZeroDateTime", StringComparison.OrdinalIgnoreCase)
            ? fixture.ConnectionString
            : fixture.ConnectionString + "ConvertZeroDateTime=True;";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = connectionString })
            .Build();

        var all = new List<Claim> { new(ClaimTypes.Name, "scoped"), new(ClaimTypes.Sid, "1") };
        all.AddRange(claims);
        var accessor = new FixedAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(all, "Bearer")) }
        };

        return new DalService(configuration, accessor);
    }

    private static async Task<int[]> ScopedEntitiesAsync(DalService dal)
    {
        await using var db = dal.GetContext();

        var sets = new[]
        {
            await db.Risks.AsNoTracking().Select(r => r.EntityId!.Value).ToListAsync(),
            await db.Vulnerabilities.AsNoTracking().Select(v => v.EntityId!.Value).ToListAsync(),
            await db.Hosts.AsNoTracking().Select(h => h.EntityId!.Value).ToListAsync(),
            await db.Incidents.AsNoTracking().Select(i => i.EntityId!.Value).ToListAsync(),
            await db.Assessments.AsNoTracking().Select(a => a.EntityId!.Value).ToListAsync()
        };

        // Every table answers the same set: one row per entity in each.
        foreach (var set in sets.Skip(1)) Assert.Equal(sets[0].Order(), set.Order());
        return sets[0].Order().ToArray();
    }

    [Fact]
    public async Task TestAClaimOnAUnitReadsItsSubtreeInSql()
    {
        await SeedTreeAsync();

        Assert.Equal(new[] { UnitX, SubX, ProcessUnderSubX },
            await ScopedEntitiesAsync(DalActingAs(new Claim("entity_id", UnitX.ToString()))));
    }

    [Fact]
    public async Task TestAClaimOnASubunitReadsNeitherItsParentNorItsSiblingsInSql()
    {
        await SeedTreeAsync();

        Assert.Equal(new[] { SubX, ProcessUnderSubX },
            await ScopedEntitiesAsync(DalActingAs(new Claim("entity_id", SubX.ToString()))));
    }

    [Fact]
    public async Task TestGlobalScopeStillReadsEverythingInSql()
    {
        await SeedTreeAsync();

        Assert.Equal(AllEntities.Order(), await ScopedEntitiesAsync(DalActingAs(new Claim("scope", "global"))));
    }

    /// <summary>The expanded set is in the statement the database runs, not applied to rows after they arrive.</summary>
    [Fact]
    public async Task TestTheExpandedScopeIsTranslatedIntoSql()
    {
        await SeedTreeAsync();

        await using var db = DalActingAs(new Claim("entity_id", UnitX.ToString())).GetContext();
        var sql = db.Risks.AsNoTracking().ToQueryString();

        Assert.Contains("entity_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { UnitX, SubX, ProcessUnderSubX }, db.ScopeEntityIds.Order());
    }

    private sealed class FixedAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
