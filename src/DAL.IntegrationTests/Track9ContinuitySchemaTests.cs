using System;
using System.IO;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using ServerServices.Governance;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.3 (S43 §5, §8 Q1–Q8) — the business-impact-analysis version on a real MariaDB: the three
/// tables and the two permissions arrive, granted to nobody; a retry converges; every CHECK and unique
/// index refuses what the service refuses; deleting the node cascades and deleting a user only clears
/// the author; the EF model round-trips; a real duplicate is recognised by its index name, which is how
/// the service turns the loser of a race into 409; and replaying the Data script never overwrites a
/// parameter an administrator changed.
///
/// The version is found by its marker, never written here (S42 §11, defect 1).
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9ContinuitySchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`business_impact_analyses`");

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
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
        Convert.ToInt64(await ScalarAsync(conn, sql));

    private static async Task ApplyVersionAsync(MySqlConnection conn, int version, params string[] parts)
    {
        foreach (var part in parts.Length == 0 ? ["Structure", "Data"] : parts)
            await ExecAsync(conn, await File.ReadAllTextAsync(
                Path.Combine(MariaDbContainerFixture.RepoDbDir(), part, $"{version}.sql")));
    }

    private static Task SeedAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
        "VALUES (930,1,0,'local','U930','u930@x.test','s',REPEAT('x',60),1,0,'user930');" +
        "INSERT INTO `entities` (`Id`,`DefinitionName`,`DefinitionVersion`,`CreatedBy`,`UpdatedBy`,`Status`) VALUES " +
        "(10,'businessProcess','2.5',930,930,'active'),(20,'itService','2.5',930,930,'active')," +
        "(21,'itService','2.5',930,930,'active');");

    private static async Task<MySqlException> RefusedAsync(MySqlConnection conn, string sql) =>
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, sql));

    /// <summary>Q1 — the upgrade creates the tables, seeds both permissions once and grants them to nobody,
    /// and seeds the two parameters at their defaults.</summary>
    [Fact]
    public async Task TestQ1_TheUpgradeCreatesTablesPermissionsAndParameters()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();

        await ApplyVersionAsync(conn, V);

        Assert.Equal(V.ToString(), await ScalarAsync(conn, "SELECT value FROM settings WHERE name = 'db_version'"));
        foreach (var table in new[] { "business_impact_analyses", "bia_dependencies", "restoration_tests" })
            Assert.Equal(1L, await CountAsync(conn,
                $"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}'"));

        foreach (var key in new[] { "bia_manage", "restoration_test_record" })
        {
            Assert.Equal(1L, await CountAsync(conn, $"SELECT COUNT(*) FROM permissions WHERE `key` = '{key}'"));
            Assert.Equal(0L, await CountAsync(conn,
                $"SELECT COUNT(*) FROM role_responsibilities r JOIN permissions p ON p.id = r.permission_id WHERE p.`key` = '{key}'"));
            Assert.Equal(0L, await CountAsync(conn,
                $"SELECT COUNT(*) FROM permission_to_user u JOIN permissions p ON p.id = u.permission_id WHERE p.`key` = '{key}'"));
        }

        Assert.Equal("365", await ScalarAsync(conn,
            $"SELECT value FROM settings WHERE name = '{ContinuitySettingKeys.RestorationTestValidityDays}'"));
        Assert.Equal("0.5", await ScalarAsync(conn,
            $"SELECT value FROM settings WHERE name = '{ContinuitySettingKeys.UnverifiedThreatWeight}'"));
    }

    /// <summary>Q2 — Structure applied twice and Data again (db_version back at v − 1, as after a failed
    /// Data script) converge: one history row, one row per permission and per parameter.</summary>
    [Fact]
    public async Task TestQ2_ReplayingTheVersionConverges()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();

        await ApplyVersionAsync(conn, V);
        await ExecAsync(conn, $"UPDATE settings SET value = '{V - 1}' WHERE name = 'db_version';");
        await ApplyVersionAsync(conn, V, "Structure");
        await ApplyVersionAsync(conn, V, "Structure");
        await ApplyVersionAsync(conn, V, "Data");

        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9BusinessImpactAnalysis'"));
        Assert.Equal(2L, await CountAsync(conn,
            "SELECT COUNT(*) FROM permissions WHERE `key` IN ('bia_manage','restoration_test_record')"));
        Assert.Equal(2L, await CountAsync(conn,
            "SELECT COUNT(*) FROM settings WHERE name LIKE 'continuity\\_%'"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND INDEX_NAME = 'uq_bia_dependencies_dependent_entity_id_provider_entity_id' AND SEQ_IN_INDEX = 1"));
    }

    /// <summary>Q3 — every CHECK refuses what the service refuses.</summary>
    [Fact]
    public async Task TestQ3_TheChecksRefuseInvalidRows()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        const string bia = "INSERT INTO business_impact_analyses (entity_id, mtpd_minutes, rto_minutes, rpo_minutes, assessed_at, created_at) VALUES ";
        await RefusedAsync(conn, bia + "(10, NULL, NULL, NULL, UTC_TIMESTAMP(), UTC_TIMESTAMP());");
        await RefusedAsync(conn, bia + "(10, NULL, -1, NULL, UTC_TIMESTAMP(), UTC_TIMESTAMP());");
        await RefusedAsync(conn, bia + "(10, 240, 241, NULL, UTC_TIMESTAMP(), UTC_TIMESTAMP());");
        await ExecAsync(conn, bia + "(10, 240, 240, 0, UTC_TIMESTAMP(), UTC_TIMESTAMP());");

        await RefusedAsync(conn,
            "INSERT INTO bia_dependencies (dependent_entity_id, provider_entity_id, created_at) VALUES (10, 10, UTC_TIMESTAMP());");

        const string test = "INSERT INTO restoration_tests (entity_id, tested_at, outcome, achieved_rto_minutes, achieved_rpo_minutes, created_at, voided_at, void_reason) VALUES ";
        await RefusedAsync(conn, test + "(10, UTC_TIMESTAMP(), 1, NULL, NULL, UTC_TIMESTAMP(), NULL, NULL);");
        await RefusedAsync(conn, test + "(10, UTC_TIMESTAMP(), 1, -5, NULL, UTC_TIMESTAMP(), NULL, NULL);");
        await RefusedAsync(conn, test + "(10, UTC_TIMESTAMP(), 2, NULL, NULL, UTC_TIMESTAMP(), UTC_TIMESTAMP(), NULL);");
        await RefusedAsync(conn, test + "(10, UTC_TIMESTAMP(), 2, NULL, NULL, UTC_TIMESTAMP(), NULL, 'a reason here');");
        await ExecAsync(conn, test + "(10, UTC_TIMESTAMP(), 2, NULL, NULL, UTC_TIMESTAMP(), NULL, NULL);");
    }

    /// <summary>Q4 — one BIA per node, one row per dependency pair.</summary>
    [Fact]
    public async Task TestQ4_TheUniqueIndexesRefuseDuplicates()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        const string bia = "INSERT INTO business_impact_analyses (entity_id, rto_minutes, assessed_at, created_at) VALUES (10, 60, UTC_TIMESTAMP(), UTC_TIMESTAMP());";
        await ExecAsync(conn, bia);
        Assert.Contains("uq_business_impact_analyses_entity_id", (await RefusedAsync(conn, bia)).Message);

        const string dependency = "INSERT INTO bia_dependencies (dependent_entity_id, provider_entity_id, created_at) VALUES (10, 20, UTC_TIMESTAMP());";
        await ExecAsync(conn, dependency);
        Assert.Contains("uq_bia_dependencies_dependent_entity_id_provider_entity_id", (await RefusedAsync(conn, dependency)).Message);
    }

    /// <summary>Q5 — deleting a node removes its BIA, its dependencies on both sides and its tests;
    /// deleting a user only clears the author.</summary>
    [Fact]
    public async Task TestQ5_DeletingTheNodeCascadesAndDeletingTheUserClearsTheAuthor()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        await ExecAsync(conn,
            "INSERT INTO business_impact_analyses (entity_id, rto_minutes, assessed_at, created_at, created_by_id) VALUES (20, 60, UTC_TIMESTAMP(), UTC_TIMESTAMP(), 930);" +
            "INSERT INTO business_impact_analyses (entity_id, rto_minutes, assessed_at, created_at, created_by_id) VALUES (21, 60, UTC_TIMESTAMP(), UTC_TIMESTAMP(), 930);" +
            "INSERT INTO bia_dependencies (dependent_entity_id, provider_entity_id, created_at) VALUES (10, 20, UTC_TIMESTAMP()), (20, 21, UTC_TIMESTAMP());" +
            "INSERT INTO restoration_tests (entity_id, tested_at, outcome, achieved_rto_minutes, created_at, recorded_by_id) VALUES (20, UTC_TIMESTAMP(), 1, 30, UTC_TIMESTAMP(), 930);");

        await ExecAsync(conn, "DELETE FROM entities WHERE Id = 20;");

        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM business_impact_analyses WHERE entity_id = 20"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM bia_dependencies"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM restoration_tests"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM business_impact_analyses WHERE entity_id = 21"));

        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 930;");
        Assert.Null(await ScalarAsync(conn, "SELECT created_by_id FROM business_impact_analyses WHERE entity_id = 21"));
    }

    /// <summary>Q6 — the EF model round-trips the three entities, the outcome reaching the database as an
    /// integer.</summary>
    [Fact]
    public async Task TestQ6_TheModelRoundTrips()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using (var conn = await OpenAsync()) await SeedAsync(conn);

        var at = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc);
        await using (var write = fixture.NewContext())
        {
            write.BusinessImpactAnalyses.Add(new BusinessImpactAnalysis
                { EntityId = 10, MtpdMinutes = 480, RtoMinutes = 240, RpoMinutes = 0, AssessedAt = at, CreatedAt = at, CreatedById = 930 });
            write.BiaDependencies.Add(new BiaDependency { DependentEntityId = 10, ProviderEntityId = 20, Description = "portal", CreatedAt = at });
            write.RestorationTests.Add(new RestorationTest
                { EntityId = 10, TestedAt = at, Outcome = RestorationTestOutcome.Failed, CreatedAt = at, VoidedAt = at, VoidReason = "Wrong process entirely" });
            await write.SaveChangesAsync();
        }

        await using (var read = fixture.NewContext())
        {
            var bia = await read.BusinessImpactAnalyses.SingleAsync();
            Assert.Equal((480, 240, 0), (bia.MtpdMinutes, bia.RtoMinutes, bia.RpoMinutes));
            Assert.Equal("portal", (await read.BiaDependencies.SingleAsync()).Description);
            var test = await read.RestorationTests.SingleAsync();
            Assert.Equal(RestorationTestOutcome.Failed, test.Outcome);
            Assert.Equal("Wrong process entirely", test.VoidReason);
        }

        await using var check = await OpenAsync();
        Assert.Equal(2, Convert.ToInt32(await ScalarAsync(check, "SELECT outcome FROM restoration_tests")));
    }

    /// <summary>Q7 — a real duplicate is a <see cref="DbUpdateException"/> naming the unique index, which is
    /// exactly what <see cref="ContinuityService"/> recognises to answer the loser of a race with 409.</summary>
    [Fact]
    public async Task TestQ7_ARealDuplicateIsRecognisedByItsIndexName()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using (var conn = await OpenAsync()) await SeedAsync(conn);

        var at = DateTime.UtcNow;
        await using (var first = fixture.NewContext())
        {
            first.BusinessImpactAnalyses.Add(new BusinessImpactAnalysis { EntityId = 10, RtoMinutes = 60, AssessedAt = at, CreatedAt = at });
            first.BiaDependencies.Add(new BiaDependency { DependentEntityId = 10, ProviderEntityId = 20, CreatedAt = at });
            await first.SaveChangesAsync();
        }

        await using (var second = fixture.NewContext())
        {
            second.BusinessImpactAnalyses.Add(new BusinessImpactAnalysis { EntityId = 10, RtoMinutes = 30, AssessedAt = at, CreatedAt = at });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            Assert.True(ContinuityService.Mentions(ex, "uq_business_impact_analyses_entity_id"));
        }

        await using (var third = fixture.NewContext())
        {
            third.BiaDependencies.Add(new BiaDependency { DependentEntityId = 10, ProviderEntityId = 20, CreatedAt = at });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => third.SaveChangesAsync());
            Assert.True(ContinuityService.Mentions(ex, "uq_bia_dependencies_dependent_entity_id_provider_entity_id"));
        }
    }

    /// <summary>Q8 — replaying the Data script keeps a parameter the administrator changed.</summary>
    [Fact]
    public async Task TestQ8_ReplayingTheDataKeepsAChangedParameter()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();

        await ExecAsync(conn,
            $"UPDATE settings SET value = '180' WHERE name = '{ContinuitySettingKeys.RestorationTestValidityDays}';" +
            $"UPDATE settings SET value = '{V - 1}' WHERE name = 'db_version';");
        await ApplyVersionAsync(conn, V, "Data");

        Assert.Equal("180", await ScalarAsync(conn,
            $"SELECT value FROM settings WHERE name = '{ContinuitySettingKeys.RestorationTestValidityDays}'"));
        Assert.Equal("0.5", await ScalarAsync(conn,
            $"SELECT value FROM settings WHERE name = '{ContinuitySettingKeys.UnverifiedThreatWeight}'"));
    }
}
