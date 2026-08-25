using System.Linq;
using System.Threading.Tasks;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Proves the multi-tenant query filters (Track 2 milestone 2.3.1/2.3.2) are enforced by the
/// <b>database</b>, not by the client.
///
/// The unit tests for this run on the EF in-memory provider, which evaluates any predicate it
/// cannot translate in memory — so they would pass even if the filter never reached SQL, and a
/// filter that silently degrades to client evaluation is a data-leak risk on a large table as
/// well as a performance one. These tests run against real MariaDB and assert both the rows
/// returned and the SQL actually generated.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class EntityScopeQueryFilterTests(MariaDbContainerFixture fixture)
{
    private const int EntityA = 100;
    private const int EntityB = 200;

    private async Task SeedTwoEntitiesAsync()
    {
        // Build to the *current* target, not a literal: the EF model maps the Track 3 columns added
        // in 77 (component, status_id, sla_due_date, …), so a schema that stops at 75 makes every
        // Vulnerability query fail with "Unknown column 'v.component'".
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await MariaDbContainerFixture.ExecAsync(conn, "SET SESSION sql_mode = '';");
        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 0;");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `risks` (`id`,`status`,`subject`,`entity_id`) VALUES " +
            $"(1,'New','risk in A',{EntityA}),(2,'New','risk in B',{EntityB}),(3,'New','unassigned',NULL);");

        // Note the casing: vulnerabilities carries `EntityId` (added in 23.sql) while the other
        // four tables got `entity_id` in 74.sql. That is pre-existing Track 6 naming drift, not a
        // typo here — EF maps the vulnerability column by convention rather than HasColumnName.
        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `vulnerabilities` (`id`,`title`,`status`,`EntityId`) VALUES " +
            $"(1,'vuln in A',1,{EntityA}),(2,'vuln in B',1,{EntityB});");

        await MariaDbContainerFixture.ExecAsync(conn, "SET FOREIGN_KEY_CHECKS = 1;");
    }

    [Fact]
    public async Task ScopedContextReturnsOnlyTheCallersEntity()
    {
        await SeedTwoEntitiesAsync();

        await using var scoped = fixture.NewScopedContext(EntityA);

        var risks = await scoped.Risks.AsNoTracking().OrderBy(r => r.Id).ToListAsync();

        Assert.Single(risks);
        Assert.Equal(1, risks[0].Id);
    }

    /// <summary>
    /// The point of the whole exercise: the predicate must be in the SQL. If EF ever stops
    /// translating it, the rows above would still come back correct while every row in the table
    /// crossed the wire first.
    /// </summary>
    [Fact]
    public async Task TheFilterIsTranslatedIntoSql()
    {
        await SeedTwoEntitiesAsync();

        await using var scoped = fixture.NewScopedContext(EntityA);

        var sql = scoped.Risks.AsNoTracking().ToQueryString();

        Assert.Contains("entity_id", sql, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, System.StringComparison.OrdinalIgnoreCase);

        // And the scoped id itself must reach the query, either inlined or as a parameter.
        Assert.True(
            sql.Contains(EntityA.ToString()) || sql.Contains("@__", System.StringComparison.Ordinal),
            $"The scope was not present in the generated SQL:\n{sql}");
    }

    [Fact]
    public async Task AnUnassignedRowIsNotVisibleToAScopedCaller()
    {
        await SeedTwoEntitiesAsync();

        await using var scoped = fixture.NewScopedContext(EntityA);

        // Row 3 has a null entity_id. A scoped caller must not see it: null means "not yet
        // assigned", which is not the same as "belongs to everyone".
        Assert.Null(await scoped.Risks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 3));
    }

    [Fact]
    public async Task FindDoesNotSlipPastTheFilter()
    {
        await SeedTwoEntitiesAsync();

        await using var scoped = fixture.NewScopedContext(EntityA);

        // Find is the method most likely to bypass a filter, because it can short-circuit on the
        // change tracker. On a fresh context it must issue a filtered query.
        Assert.Null(await scoped.Risks.FindAsync(2));
        Assert.NotNull(await scoped.Risks.FindAsync(1));
    }

    [Fact]
    public async Task VulnerabilitiesAreScopedToo()
    {
        await SeedTwoEntitiesAsync();

        await using var scoped = fixture.NewScopedContext(EntityA);

        var vulns = await scoped.Vulnerabilities.AsNoTracking().ToListAsync();

        Assert.Single(vulns);
        Assert.Equal(1, vulns[0].Id);
    }

    [Fact]
    public async Task AUserAssignedToBothEntitiesSeesBoth()
    {
        await SeedTwoEntitiesAsync();

        await using var scoped = fixture.NewScopedContext(EntityA, EntityB);

        Assert.Equal(2, await scoped.Risks.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task AUserWithNoAssignmentSeesNothing()
    {
        await SeedTwoEntitiesAsync();

        await using var denied = fixture.NewDenyAllContext();

        Assert.Empty(await denied.Risks.AsNoTracking().ToListAsync());
        Assert.Empty(await denied.Vulnerabilities.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AnUnrestrictedContextSeesEverything()
    {
        await SeedTwoEntitiesAsync();

        await using var admin = fixture.NewContext();

        // Including the unassigned row, which is what makes the Master Dashboard's totals
        // reconcile with the per-module screens.
        Assert.Equal(3, await admin.Risks.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task AScopedCallerCannotUpdateAnotherEntitysRow()
    {
        await SeedTwoEntitiesAsync();

        await using (var scoped = fixture.NewScopedContext(EntityA))
        {
            // The row cannot even be materialised, so there is nothing to attach and save.
            var target = await scoped.Risks.FirstOrDefaultAsync(r => r.Id == 2);
            Assert.Null(target);
        }

        await using var admin = fixture.NewContext();
        var untouched = await admin.Risks.AsNoTracking().FirstAsync(r => r.Id == 2);
        Assert.Equal("risk in B", untouched.Subject);
    }
}
