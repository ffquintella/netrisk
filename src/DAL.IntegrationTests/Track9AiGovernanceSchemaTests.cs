using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.12 (S53 §4, §8 Q1–Q4) — the AI governance version on a real MariaDB: the five tables arrive empty, the write
/// permission is seeded once and granted to nobody, and a retry converges without a second row; the CHECKs and unique
/// indexes refuse what the services refuse — one model per name (case-insensitively), the enumerations, a retirement only
/// with its date and reason, a metric value in its range, an override rate only with its period, sample and count —, which
/// the in-memory provider cannot enforce; a model with evidence (a reading, an override, a risk link) and a vendor named by a
/// model cannot be deleted (RESTRICT), while a data node takes its data links and a deleted risk or user releases what
/// pointed at it. Q4 is the database half of T215: an acceptance naming a non-user, and a review by one, are refused by the
/// foreign keys the decision tables have always had.
///
/// The version is found by its marker, never written here (S42 §11, defect 1). Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9AiGovernanceSchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`ai_model_risks`");

    private static readonly string[] Tables =
        ["ai_models", "ai_model_data_links", "ai_model_metric_readings", "ai_model_overrides", "ai_model_risks"];

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

    /// <summary>User 990; data record 9901 (<c>organizationData</c>); third party 9911; risk 9921; model 9931 in production.</summary>
    private static Task SeedAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
        "VALUES (990,1,0,'local','U990','u990@x.test','s',REPEAT('x',60),1,0,'user990');" +
        "INSERT INTO entities (Id, DefinitionName, DefinitionVersion, Created, Updated, CreatedBy, UpdatedBy, Status, Parent) VALUES " +
        "(9901,'organizationData','2.6',NOW(),NOW(),990,990,'active',NULL);" +
        "INSERT INTO third_parties (id, name, status, is_cloud_provider, is_identity_provider, created_at) " +
        "VALUES (9911, 'Model Vendor', 2, 0, 0, NOW());" +
        "INSERT INTO `risks` (`id`,`status`) VALUES (9921,'New');" +
        Model(9931, "Admissions triage") + ";");

    private static string Model(int id, string name, string extra = "", string values = "") =>
        $"INSERT INTO ai_models (id, name, purpose, kind, source, version, status, max_evaluation_age_days, created_at{extra}) " +
        $"VALUES ({id}, '{name}', 'Ranks applications.', 1, 1, '2.1', 3, 90, NOW(){values})";

    private static string Reading(int metric, string value, string extra = "", string values = "", int modelId = 9931) =>
        $"INSERT INTO ai_model_metric_readings (model_id, metric, value, model_version, measured_at, created_at{extra}) " +
        $"VALUES ({modelId}, {metric}, {value}, '2.1', NOW(), NOW(){values})";

    private static string Override(string extra = "", string values = "", int modelId = 9931) =>
        $"INSERT INTO ai_model_overrides (model_id, model_version, occurred_at, model_output, human_decision, reason, created_at{extra}) " +
        $"VALUES ({modelId}, '2.1', NOW(), 'Reject', 'Admit', 'The transcript was misread.', NOW(){values})";

    /// <summary>
    /// Q1 — the upgrade creates the five tables empty, seeds <c>ai_governance_manage</c> once and grants it to nobody, and a
    /// replay (Structure twice, Data again) converges without a second row.
    /// </summary>
    [Fact]
    public async Task TestQ1_TheUpgradeCreatesTheSchemaSeedsThePermissionOnceAndConverges()
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

        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM permissions WHERE `key` = 'ai_governance_manage'"));
        Assert.Equal(0L, await CountAsync(conn,
            "SELECT COUNT(*) FROM role_responsibilities rr JOIN permissions p ON p.id = rr.permission_id WHERE p.`key` = 'ai_governance_manage'"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9AiGovernance'"));
    }

    /// <summary>Q2 — the CHECKs and unique indexes refuse what the services refuse.</summary>
    [Fact]
    public async Task TestQ2_ConstraintsRefuseWhatTheServicesRefuse()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        // One model per name, case-insensitively (the collation); the enumerations; the evaluation's maximum age 1–1096.
        Assert.Contains("uq_ai_models_name", (await RefusedAsync(conn, Model(9932, "ADMISSIONS TRIAGE"))).Message);
        await RefusedAsync(conn, Model(9932, "Kind 8").Replace("VALUES (9932, 'Kind 8', 'Ranks applications.', 1,", "VALUES (9932, 'Kind 8', 'Ranks applications.', 8,"));
        await RefusedAsync(conn, Model(9932, "Tier 4", ", risk_tier", ", 4"));
        await RefusedAsync(conn, Model(9932, "Oversight 0", ", human_oversight", ", 0"));
        await RefusedAsync(conn, Model(9932, "Age 0").Replace(", 3, 90, NOW()", ", 3, 0, NOW()"));
        await RefusedAsync(conn, Model(9932, "Age 1097").Replace(", 3, 90, NOW()", ", 3, 1097, NOW()"));
        await ExecAsync(conn, Model(9932, "Tiered", ", risk_tier, human_oversight", ", 3, 3"));

        // Retired exactly with its date and reason.
        await RefusedAsync(conn, Model(9933, "Retired bare").Replace(", 3, 90, NOW()", ", 4, 90, NOW()"));
        await RefusedAsync(conn, Model(9933, "Retired dated", ", retired_at", ", NOW()").Replace(", 3, 90, NOW()", ", 4, 90, NOW()"));
        await RefusedAsync(conn, Model(9933, "Live but dated", ", retired_at", ", NOW()"));
        await ExecAsync(conn, Model(9933, "Retired", ", retired_at, retire_reason", ", NOW(), 'Replaced by v3.'")
            .Replace(", 3, 90, NOW()", ", 4, 90, NOW()"));

        // Data links: use 1–5, one per model, record and use.
        await ExecAsync(conn, "INSERT INTO ai_model_data_links (model_id, entity_id, data_usage, created_at) VALUES (9931, 9901, 1, NOW())");
        Assert.Contains("uq_ai_model_data_links_model_id_entity_id_data_usage", (await RefusedAsync(conn,
            "INSERT INTO ai_model_data_links (model_id, entity_id, data_usage, created_at) VALUES (9931, 9901, 1, NOW())")).Message);
        await RefusedAsync(conn, "INSERT INTO ai_model_data_links (model_id, entity_id, data_usage, created_at) VALUES (9931, 9901, 6, NOW())");
        await ExecAsync(conn, "INSERT INTO ai_model_data_links (model_id, entity_id, data_usage, created_at) VALUES (9931, 9901, 4, NOW())");

        // Readings: metric 1–6; a fraction but for drift; the period whole and forward; a sample of one or more.
        await RefusedAsync(conn, Reading(7, "0.5"));
        await RefusedAsync(conn, Reading(1, "1.01"));
        await RefusedAsync(conn, Reading(4, "-0.1"));
        await ExecAsync(conn, Reading(5, "3.5"));
        await ExecAsync(conn, Reading(1, "1"));
        await RefusedAsync(conn, Reading(1, "0.9", ", period_start", ", NOW()"));
        await RefusedAsync(conn, Reading(1, "0.9", ", period_start, period_end", ", NOW(), NOW() - INTERVAL 1 DAY"));
        await RefusedAsync(conn, Reading(1, "0.9", ", sample_size", ", 0"));

        // The override rate: only with its period, its sample and its count, the count within the sample; nothing else has a count.
        await RefusedAsync(conn, Reading(6, "0.25"));
        await RefusedAsync(conn, Reading(6, "0.25", ", period_start, period_end, sample_size", ", NOW() - INTERVAL 30 DAY, NOW(), 12"));
        await RefusedAsync(conn, Reading(6, "0.25", ", period_start, period_end, sample_size, override_count",
            ", NOW() - INTERVAL 30 DAY, NOW(), 2, 3"));
        await ExecAsync(conn, Reading(6, "0.25", ", period_start, period_end, sample_size, override_count",
            ", NOW() - INTERVAL 30 DAY, NOW(), 12, 3"));
        await RefusedAsync(conn, Reading(1, "0.9", ", override_count", ", 1"));

        // A void carries its reason, for a reading and for an override.
        await RefusedAsync(conn, Reading(1, "0.9", ", voided_at", ", NOW()"));
        await ExecAsync(conn, Reading(1, "0.9", ", voided_at, void_reason", ", NOW(), 'Measured on the training set.'"));
        await RefusedAsync(conn, Override(", void_reason", ", 'No date.'"));
        await ExecAsync(conn, Override());

        // One link per model and risk.
        await ExecAsync(conn, "INSERT INTO ai_model_risks (model_id, risk_id, note, created_at) VALUES (9931, 9921, 'Bias', NOW())");
        Assert.Contains("uq_ai_model_risks_model_id_risk_id", (await RefusedAsync(conn,
            "INSERT INTO ai_model_risks (model_id, risk_id, created_at) VALUES (9931, 9921, NOW())")).Message);
    }

    /// <summary>
    /// Q3 — a model with evidence is not deleted by the database either (a reading, an override and a risk link each
    /// RESTRICT); a vendor named by a model is not deleted; a deleted data node takes its data links; a deleted risk takes its
    /// model links; a deleted user clears the authorship and deletes nothing.
    /// </summary>
    [Fact]
    public async Task TestQ3_EvidenceIsRestrictedDeclarationsCascadeReferencesRelease()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        await ExecAsync(conn, Reading(5, "0.1"));
        await RefusedAsync(conn, "DELETE FROM ai_models WHERE id = 9931");
        await ExecAsync(conn, "DELETE FROM ai_model_metric_readings WHERE model_id = 9931");

        await ExecAsync(conn, Override());
        await RefusedAsync(conn, "DELETE FROM ai_models WHERE id = 9931");
        await ExecAsync(conn, "DELETE FROM ai_model_overrides WHERE model_id = 9931");

        await ExecAsync(conn, "INSERT INTO ai_model_risks (model_id, risk_id, created_at) VALUES (9931, 9921, NOW())");
        await RefusedAsync(conn, "DELETE FROM ai_models WHERE id = 9931");

        // The vendor is in use while a model names it.
        await ExecAsync(conn, Model(9932, "Vendor model", ", third_party_id", ", 9911").Replace(", 1, 1, '2.1'", ", 1, 2, '2.1'"));
        await RefusedAsync(conn, "DELETE FROM third_parties WHERE id = 9911");

        // The data node takes its links; the model stays.
        await ExecAsync(conn, "INSERT INTO ai_model_data_links (model_id, entity_id, data_usage, created_at) VALUES (9931, 9901, 1, NOW())");
        await ExecAsync(conn, "DELETE FROM entities WHERE Id = 9901");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM ai_model_data_links"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM ai_models WHERE id = 9931"));

        // The risk takes its model link; the model stays.
        await ExecAsync(conn, "DELETE FROM `risks` WHERE id = 9921");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM ai_model_risks"));

        // The user clears authorship and ownership, deleting nothing.
        await ExecAsync(conn, "UPDATE ai_models SET owner_id = 990, created_by_id = 990, updated_by_id = 990 WHERE id = 9931");
        await ExecAsync(conn, Override(", recorded_by_id", ", 990"));
        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 990");
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM ai_models WHERE id = 9931 AND owner_id IS NULL AND created_by_id IS NULL AND updated_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM ai_model_overrides WHERE recorded_by_id IS NULL"));
    }

    /// <summary>
    /// Q4 (T215, the database half) — residual risk is accepted and reviewed only by a person, by the foreign keys the
    /// decision tables have always had: an acceptance naming a non-user as its authorizing manager
    /// (<c>fk_ra_authorizing_manager_id</c>) and a management review by a non-user reviewer (<c>fw_rev</c>) are refused — the
    /// background actor's id 0 included —, and the same rows naming a user are accepted.
    /// </summary>
    [Fact]
    public async Task TestQ4_TheDecisionTablesReferAPerson()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        static string Acceptance(int manager) =>
            "INSERT INTO risk_acceptances (name, risk_id, authorizing_manager_id, expires_at, created_at) " +
            $"VALUES ('Exception', 9921, {manager}, NOW() + INTERVAL 90 DAY, NOW())";

        static string Review(int reviewer) =>
            "INSERT INTO mgmt_reviews (risk_id, submission_date, review, reviewer, next_step, comments, next_review) " +
            $"VALUES (9921, NOW(), 1, {reviewer}, 1, 'Reviewed.', CURDATE())";

        Assert.Contains("fk_ra_authorizing_manager_id", (await RefusedAsync(conn, Acceptance(0))).Message);
        await RefusedAsync(conn, Acceptance(99999));
        Assert.Contains("fw_rev", (await RefusedAsync(conn, Review(0))).Message);
        await RefusedAsync(conn, Review(99999));

        await ExecAsync(conn, Acceptance(990));
        await ExecAsync(conn, Review(990));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_acceptances WHERE authorizing_manager_id = 990"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM mgmt_reviews WHERE reviewer = 990"));
    }
}
