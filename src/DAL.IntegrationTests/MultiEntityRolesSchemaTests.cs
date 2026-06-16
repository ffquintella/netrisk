using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Regression test for the production auth outage: <c>BasicAuthenticationHandler</c>/<c>JwtAuthenticationHandler</c>
/// query <c>user_entity_roles</c> on every login, but the multi-entity scoped roles feature shipped in 2.11.0
/// without its schema, so authentication crashed with "Table 'netrisk.user_entity_roles' doesn't exist".
/// This applies the full numbered SQL up to db_version 74 (the fix) and asserts the table — and the other
/// tables/columns that drifted in alongside it — now exist and the exact auth query runs.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class MultiEntityRolesSchemaTests(MariaDbContainerFixture fixture)
{
    [Fact]
    public async Task NumberedSchema_To74_CreatesMultiEntityAndRelatedTables()
    {
        await fixture.InitializeNumberedSchemaAsync(74);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        foreach (var table in new[]
                 {
                     "user_entity_roles", "report_templates", "report_template_versions",
                     "report_schedules", "irp_templates", "irp_template_tasks", "assessment_run_answers",
                 })
        {
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @t", conn);
            cmd.Parameters.AddWithValue("@t", table);
            Assert.True(Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1, $"expected table '{table}' to exist after upgrade to 74");
        }

        // The entity_id scoping columns added to existing tables.
        foreach (var (table, column) in new[]
                 {
                     ("risks", "entity_id"), ("incidents", "entity_id"), ("hosts", "entity_id"),
                     ("assessments", "entity_id"), ("assessment_questions", "parent_question_id"),
                 })
        {
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @t AND column_name = @c", conn);
            cmd.Parameters.AddWithValue("@t", table);
            cmd.Parameters.AddWithValue("@c", column);
            Assert.True(Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1, $"expected column '{table}.{column}' to exist after upgrade to 74");
        }

        await using var verCmd = new MySqlCommand("SELECT value FROM settings WHERE name='db_version'", conn);
        Assert.Equal("74", (await verCmd.ExecuteScalarAsync())?.ToString());
    }

    [Fact]
    public async Task UserEntityRoles_AuthLookupQuery_Succeeds()
    {
        await fixture.InitializeNumberedSchemaAsync(74);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        // The exact shape of the query the auth handlers run on every login
        // (BasicAuthenticationHandler.cs: UserEntityRoles.Where(uer => uer.UserId == x && uer.RevokedAt == null)).
        // Before the fix this threw MySqlException; now it must execute and return zero rows.
        await using var cmd = new MySqlCommand(
            "SELECT entity_id FROM user_entity_roles WHERE user_id = @uid AND revoked_at IS NULL", conn);
        cmd.Parameters.AddWithValue("@uid", 50);

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync());
    }
}
