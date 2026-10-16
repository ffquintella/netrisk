using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// M53, T243 and T293 on a real MariaDB (S40 §12.7): the version creates the three index tables, seeds the three
/// permissions, the two approver roles and their grants (nothing for Administrator) and the two settings, and converges
/// on a replay; the CHECKs, unique keys and RESTRICT hold what the lifecycle needs; the EF model reads and writes the
/// tables, the revision is a real concurrency token, and a scoped context sees only its own snapshot and object rows.
/// The version is found by its marker, never written here (S42 §11, defect 1). Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class RiskIndexSchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("CREATE TABLE IF NOT EXISTS `risk_index_object_days`");

    private static readonly string[] Tables = ["risk_index_profiles", "risk_index_snapshots", "risk_index_object_days"];

    private static readonly string[] IndexPermissions =
        [RiskIndexPermissions.View, RiskIndexPermissions.Configure, RiskIndexPermissions.Approve];

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
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string?> TextAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private static async Task ApplyVersionAsync(MySqlConnection conn, int version, params string[] parts)
    {
        foreach (var part in parts.Length == 0 ? ["Structure", "Data"] : parts)
            await ExecAsync(conn, await File.ReadAllTextAsync(
                Path.Combine(MariaDbContainerFixture.RepoDbDir(), part, $"{version}.sql")));
    }

    private static async Task<HashSet<(string Role, string Key)>> IndexGrantsAsync(MySqlConnection conn)
    {
        var keys = string.Join(",", IndexPermissions.Append(Model.Entities.EntityPermissions.RiskContext).Select(k => $"'{k}'"));
        await using var cmd = new MySqlCommand(
            "SELECT r.`name`, p.`key` FROM role_responsibilities rr JOIN `role` r ON r.`value` = rr.role_id " +
            $"JOIN permissions p ON p.id = rr.permission_id WHERE p.`key` IN ({keys})", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var grants = new HashSet<(string, string)>();
        while (await reader.ReadAsync()) grants.Add((reader.GetString(0), reader.GetString(1)));
        return grants;
    }

    private static HashSet<(string Role, string Key)> ExpectedGrants() =>
        RiskIndexRoles.SeededGrants.SelectMany(g => g.Value.Select(key => (g.Key, key))).ToHashSet();

    /// <summary>User 990; profile 1 (draft, version 1).</summary>
    private static Task SeedUserAndDraftAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
        "VALUES (990,1,0,'local','U990','u990@x.test','s',REPEAT('x',60),1,0,'user990');" +
        Profile(1, 1, 1) + ";");

    private static string Profile(int id, int version, int status, string extra = "", string values = "") =>
        "INSERT INTO risk_index_profiles (id, version, name, mode, status, parameters, created_at, created_by_id" + extra +
        $") VALUES ({id}, {version}, 'Profile {version}', 1, {status}, '{{}}', NOW(), 990{values})";

    private static string Snapshot(int profileId, string scopeRef, int category, string entityId = "NULL", string extra = "",
        string values = "") =>
        "INSERT INTO risk_index_snapshots (snapshot_date, profile_id, mode, scope_kind, scope_ref, entity_id, category, " +
        "value_unfloored, value_floored, value_displayed, coverage, freshness, quality, object_count, presumption_points, " +
        "external_inputs_hash, created_at" + extra + ") " +
        $"VALUES ('2026-10-15', {profileId}, 1, 2, '{scopeRef}', {entityId}, {category}, 42.4, 42.4, 42, 0.9, 0.8, 0.7, 3, 0, " +
        $"REPEAT('a', 64), NOW(){values})";

    private static string ObjectDay(int profileId, string objectRef, int category, int criticality, string entityId = "NULL") =>
        "INSERT INTO risk_index_object_days (snapshot_date, profile_id, object_kind, object_ref, category, population, " +
        "r_value, weight, criticality, criticality_source, state, entity_id, attribution, factors, created_at) " +
        $"VALUES ('2026-10-15', {profileId}, 2, '{objectRef}', {category}, 2, 0.5, 1.0, {criticality}, 'default', 1, " +
        $"{entityId}, '[]', '{{}}', NOW())";

    private static async Task<MySqlException> RefusedAsync(MySqlConnection conn, string sql) =>
        await Assert.ThrowsAsync<MySqlException>(() => ExecAsync(conn, sql));

    /// <summary>
    /// Q1 — the upgrade creates the tables empty, seeds each permission and role once, grants exactly the S40 §11.1 table
    /// (Administrator and the third line nothing), seeds the settings, and a replay (Structure twice, Data again) converges.
    /// </summary>
    [Fact]
    public async Task TestQ1_TheUpgradeSeedsPermissionsRolesGrantsAndSettingsOnceAndConverges()
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

        foreach (var key in IndexPermissions)
            Assert.Equal(1L, await CountAsync(conn, $"SELECT COUNT(*) FROM permissions WHERE `key` = '{key}'"));

        foreach (var role in new[] { RiskIndexRoles.RiskManager, RiskIndexRoles.RiskAdministrator })
            Assert.Equal(1L, await CountAsync(conn, $"SELECT COUNT(*) FROM `role` WHERE `name` = '{role}' AND `admin` = 0"));

        // Exactly the grant table, plus entity_risk_context's Administrator grant from version 104 — and no index permission
        // for Administrator or the third line.
        var grants = await IndexGrantsAsync(conn);
        var expected = ExpectedGrants();
        expected.Add((RiskIndexRoles.Administrator, Model.Entities.EntityPermissions.RiskContext));
        Assert.Equal(expected.OrderBy(g => g.Role).ThenBy(g => g.Key), grants.OrderBy(g => g.Role).ThenBy(g => g.Key));
        Assert.DoesNotContain(grants, g => g.Role == "ThirdLineAuditor");

        Assert.Equal(RiskIndexRoles.DefaultApproverRoles,
            await TextAsync(conn, $"SELECT value FROM settings WHERE name = '{RiskIndexSettingKeys.ApproverRoles}'"));
        Assert.Equal(RiskIndexSettingKeys.PublicationShadow,
            await TextAsync(conn, $"SELECT value FROM settings WHERE name = '{RiskIndexSettingKeys.Publication}'"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_RiskIndexTables'"));
    }

    /// <summary>Q2 — a role an installation already created under an approver name is reused, and an operator's settings are kept.</summary>
    [Fact]
    public async Task TestQ2_AnExistingRoleIsReusedAndAnOperatorsSettingsAreKept()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();
        await ExecAsync(conn,
            "INSERT INTO `role` (`name`, `admin`, `default`) VALUES ('RiskManager', 0, NULL);" +
            $"INSERT INTO settings (name, value) VALUES ('{RiskIndexSettingKeys.Publication}', 'published')," +
            $"('{RiskIndexSettingKeys.ApproverRoles}', 'ItRiskCommittee');");

        await ApplyVersionAsync(conn, V);

        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM `role` WHERE `name` = 'RiskManager'"));
        Assert.Equal(4L, await CountAsync(conn,
            "SELECT COUNT(*) FROM role_responsibilities rr JOIN `role` r ON r.`value` = rr.role_id WHERE r.`name` = 'RiskManager'"));
        Assert.Equal("published",
            await TextAsync(conn, $"SELECT value FROM settings WHERE name = '{RiskIndexSettingKeys.Publication}'"));
        Assert.Equal("ItRiskCommittee",
            await TextAsync(conn, $"SELECT value FROM settings WHERE name = '{RiskIndexSettingKeys.ApproverRoles}'"));
    }

    /// <summary>Q3 — the constraints refuse what the lifecycle and the methodology forbid.</summary>
    [Fact]
    public async Task TestQ3_ConstraintsRefuseWhatTheLifecycleForbids()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedUserAndDraftAsync(conn);

        Assert.Contains("uq_risk_index_profiles_version", (await RefusedAsync(conn, Profile(2, 1, 1))).Message);
        Assert.Contains("ck_risk_index_profiles_submitted", (await RefusedAsync(conn, Profile(2, 2, 2))).Message);
        Assert.Contains("ck_risk_index_profiles_approved", (await RefusedAsync(conn, Profile(2, 2, 3,
            ", submitted_at, justification, approved_at, effective_from", ", NOW(), 'Pilot', NOW(), NOW()"))).Message);
        Assert.Contains("ck_risk_index_profiles_rejected", (await RefusedAsync(conn, Profile(2, 2, 5,
            ", submitted_at, justification", ", NOW(), 'Pilot'"))).Message);
        Assert.Contains("ck_risk_index_profiles_retired", (await RefusedAsync(conn, Profile(2, 2, 1,
            ", retired_at", ", NOW()"))).Message);
        Assert.Contains("ck_risk_index_profiles_mode", (await RefusedAsync(conn,
            Profile(2, 2, 1).Replace("'Profile 2', 1, 1", "'Profile 2', 3, 1"))).Message);

        // An active profile with its approval, start and committee reference is accepted.
        await ExecAsync(conn, Profile(2, 2, 3,
            ", submitted_at, justification, approved_at, effective_from, committee_decision_ref",
            ", NOW(), 'Pilot', NOW(), NOW(), 'ITRC 2026-10-10'"));

        await ExecAsync(conn, Snapshot(2, "org", 0));
        Assert.Contains("uq_risk_index_snapshots_date_profile_scope_category", (await RefusedAsync(conn, Snapshot(2, "org", 0))).Message);
        Assert.Contains("ck_risk_index_snapshots_category", (await RefusedAsync(conn, Snapshot(2, "org", 7))).Message);
        Assert.Contains("ck_risk_index_snapshots_interval", (await RefusedAsync(conn,
            Snapshot(2, "org", 1, extra: ", interval_low", values: ", 10"))).Message);
        Assert.Contains("ck_risk_index_snapshots_value_displayed", (await RefusedAsync(conn,
            Snapshot(2, "org", 2).Replace("42.4, 42.4, 42,", "142.4, 142.4, 142,"))).Message);

        await ExecAsync(conn, ObjectDay(2, "host-1", 2, 3));
        Assert.Contains("uq_risk_index_object_days_date_profile_object_category",
            (await RefusedAsync(conn, ObjectDay(2, "host-1", 2, 3))).Message);
        Assert.Contains("ck_risk_index_object_days_category", (await RefusedAsync(conn, ObjectDay(2, "host-2", 0, 3))).Message);
        Assert.Contains("ck_risk_index_object_days_criticality", (await RefusedAsync(conn, ObjectDay(2, "host-2", 2, 6))).Message);

        // RESTRICT: the profile that computed a row cannot go; removing its author keeps the profile and clears the link.
        await RefusedAsync(conn, "DELETE FROM risk_index_profiles WHERE id = 2");
        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 990");
        Assert.Equal(2L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_index_profiles WHERE created_by_id IS NULL"));
    }

    /// <summary>Q4 — the EF model reads back what it writes, and two editors of one draft cannot both save.</summary>
    [Fact]
    public async Task TestQ4_TheModelRoundTripsAndTheRevisionIsAConcurrencyToken()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        var parameters = new RiskIndexParameters { P = 8, Tau = 0.9 };

        await using (var db = fixture.NewContext())
        {
            var profile = new RiskIndexProfile
            {
                Version = 1, Name = "Pilot", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Draft,
                Parameters = RiskIndexParametersJson.Serialize(parameters), CreatedAt = DateTime.UtcNow
            };
            db.RiskIndexProfiles.Add(profile);
            await db.SaveChangesAsync();

            db.RiskIndexSnapshots.Add(new RiskIndexSnapshot
            {
                SnapshotDate = new DateOnly(2026, 10, 15), ProfileId = profile.Id, Mode = RiskIndexMode.Interim,
                ScopeKind = RiskIndexScopeKind.Entity, ScopeRef = "10", EntityId = 10, Category = RiskIndexCategory.Icr,
                ValueUnfloored = 41.6, ValueFloored = 41.6, ValueDisplayed = 42, Coverage = 0.875m, Freshness = 0.9m,
                Quality = 0.66m, IntervalLow = 38.2, IntervalHigh = 47.9, ObjectCount = 12, PresumptionPoints = 1.5,
                ExternalInputsHash = new string('f', 64), TopContributions = "[]", CreatedAt = DateTime.UtcNow
            });
            db.RiskIndexObjectDays.Add(new RiskIndexObjectDay
            {
                SnapshotDate = new DateOnly(2026, 10, 15), ProfileId = profile.Id, ObjectKind = RiskIndexObjectKind.Host,
                ObjectRef = "4711", Category = RiskIndexCategory.Exp, Population = RiskIndexPopulation.Assets, RValue = 0.83,
                Weight = 1.1234m, Criticality = 5, CriticalitySource = "cmdb", State = RiskIndexObjectState.Stale,
                EntityId = 10, Attribution = "[10]", Factors = "{}", AssetClass = RiskIndexAssetClass.Server, SourceMask = 5,
                Environment = "prod", CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.NewContext())
        {
            var profile = await db.RiskIndexProfiles.SingleAsync();
            Assert.Equal((0, false, RiskIndexProfileStatus.Draft), (profile.Revision, profile.IsRebaseline, profile.Status));
            Assert.Equal((8, 0.9), (RiskIndexParametersJson.Deserialize(profile.Parameters).P,
                RiskIndexParametersJson.Deserialize(profile.Parameters).Tau));

            var snapshot = await db.RiskIndexSnapshots.SingleAsync();
            Assert.Equal((new DateOnly(2026, 10, 15), 0.875m, 47.9, RiskIndexCategory.Icr),
                (snapshot.SnapshotDate, snapshot.Coverage, snapshot.IntervalHigh, snapshot.Category));

            var row = await db.RiskIndexObjectDays.SingleAsync();
            Assert.Equal(((sbyte)5, 1.1234m, RiskIndexAssetClass.Server, RiskIndexObjectState.Stale, 5),
                (row.Criticality, row.Weight, row.AssetClass, row.State, row.SourceMask));
        }

        await using var first = fixture.NewContext();
        await using var second = fixture.NewContext();
        var mine = await first.RiskIndexProfiles.SingleAsync();
        var theirs = await second.RiskIndexProfiles.SingleAsync();

        mine.Name = "Pilot v1";
        mine.Revision++;
        await first.SaveChangesAsync();

        theirs.Name = "Pilot (stale)";
        theirs.Revision++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    /// <summary>Q5 — on real SQL a scoped context reads only its entity's node and object rows; no entity, no scope.</summary>
    [Fact]
    public async Task TestQ5_AScopedContextSeesOnlyItsOwnSnapshotAndObjectRows()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        await using (var conn = await OpenAsync())
        {
            await SeedUserAndDraftAsync(conn);
            await ExecAsync(conn,
                Snapshot(1, "org", 0) + ";" + Snapshot(1, "10", 0, "10") + ";" + Snapshot(1, "20", 0, "20") + ";" +
                ObjectDay(1, "h-none", 2, 3) + ";" + ObjectDay(1, "h-10", 2, 3, "10") + ";" + ObjectDay(1, "h-20", 2, 3, "20") + ";");
        }

        await using (var scoped = fixture.NewScopedContext(10))
        {
            Assert.Equal(new[] { "10" }, await scoped.RiskIndexSnapshots.Select(s => s.ScopeRef).ToListAsync());
            Assert.Equal(new[] { "h-10" }, await scoped.RiskIndexObjectDays.Select(o => o.ObjectRef).ToListAsync());
        }

        await using (var denied = fixture.NewDenyAllContext())
        {
            Assert.Empty(await denied.RiskIndexSnapshots.ToListAsync());
            Assert.Empty(await denied.RiskIndexObjectDays.ToListAsync());
        }

        await using var unrestricted = fixture.NewContext();
        Assert.Equal(3, await unrestricted.RiskIndexSnapshots.CountAsync());
        Assert.Equal(3, await unrestricted.RiskIndexObjectDays.CountAsync());
    }
}
