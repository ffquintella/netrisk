using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.10 (S51 §5, §8 Q1–Q3) — the third-party register version on a real MariaDB: the eight tables arrive empty, the
/// write permission is seeded once and a retry converges without a second row; the CHECKs and unique indexes refuse what
/// the services refuse — one supplier per name (case-insensitively), one answer per question, one SBOM per document, one
/// location per country and purpose —, which the in-memory provider cannot enforce; and a third party in use cannot be
/// deleted (RESTRICT), while its own declarations go with it and a deleted service, process or user releases what pointed
/// at it.
///
/// The version is found by its marker, never written here (S42 §11, defect 1). Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9ThirdPartySchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`third_party_sbom_components`");

    private static readonly string[] Tables =
    [
        "third_parties", "third_party_links", "third_party_subprocessors", "third_party_data_locations",
        "third_party_assessments", "third_party_assessment_answers", "third_party_sboms", "third_party_sbom_components"
    ];

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

    /// <summary>User 990, service 9901, process 9902, unit 9903; third parties 9911 (org) and 9912 (cloud, unit 9903).</summary>
    private static Task SeedAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
        "VALUES (990,1,0,'local','U990','u990@x.test','s',REPEAT('x',60),1,0,'user990');" +
        "INSERT INTO entities (Id, DefinitionName, DefinitionVersion, Created, Updated, CreatedBy, UpdatedBy, Status, Parent) VALUES " +
        "(9901,'itService','2.6',NOW(),NOW(),990,990,'active',NULL),(9902,'businessProcess','2.6',NOW(),NOW(),990,990,'active',NULL)," +
        "(9903,'organizationUnit','2.6',NOW(),NOW(),990,990,'active',NULL);" +
        "INSERT INTO third_parties (id, name, status, is_cloud_provider, is_identity_provider, owner_id, created_at, created_by_id) " +
        "VALUES (9911, 'Acme SaaS', 2, 0, 0, 990, NOW(), 990);" +
        "INSERT INTO third_parties (id, name, entity_id, status, is_cloud_provider, is_identity_provider, created_at) " +
        "VALUES (9912, 'Hyperscaler', 9903, 2, 1, 0, NOW());");

    private static string Party(int id, string name, int status = 2, string terminatedAt = "NULL", string extra = "", string values = "") =>
        $"INSERT INTO third_parties (id, name, status, is_cloud_provider, is_identity_provider, terminated_at, created_at{extra}) " +
        $"VALUES ({id}, '{name}', {status}, 0, 0, {terminatedAt}, NOW(){values})";

    /// <summary>
    /// Q1 — the upgrade creates the eight tables empty, seeds <c>third_party_manage</c> once, and a replay (Structure twice,
    /// Data again) converges without a second row.
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

        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM permissions WHERE `key` = 'third_party_manage'"));
        Assert.Equal(0L, await CountAsync(conn,
            "SELECT COUNT(*) FROM role_responsibilities rr JOIN permissions p ON p.id = rr.permission_id WHERE p.`key` = 'third_party_manage'"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9ThirdParties'"));
    }

    /// <summary>Q2 — the CHECKs and unique indexes refuse what the services refuse.</summary>
    [Fact]
    public async Task TestQ2_ConstraintsRefuseWhatTheServicesRefuse()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        // One row per supplier, case-insensitively (the collation): two rows would split its concentration.
        Assert.Contains("uq_third_parties_name", (await RefusedAsync(conn, Party(9913, "ACME saas"))).Message);

        // Status 1–4; terminated exactly with its date; SLA in (0, 100]; objectives not negative; the contract ends after it starts.
        await RefusedAsync(conn, Party(9914, "S5", status: 5));
        await RefusedAsync(conn, Party(9915, "T-no-date", status: 4));
        await RefusedAsync(conn, Party(9916, "Active-dated", status: 2, terminatedAt: "NOW()"));
        await RefusedAsync(conn, Party(9917, "SLA-0", extra: ", sla_availability_percent", values: ", 0"));
        await RefusedAsync(conn, Party(9918, "SLA-101", extra: ", sla_availability_percent", values: ", 100.5"));
        await RefusedAsync(conn, Party(9919, "RTO-neg", extra: ", contracted_rto_minutes", values: ", -1"));
        await RefusedAsync(conn, Party(9920, "Backwards", extra: ", contract_start, contract_end",
            values: ", NOW(), NOW() - INTERVAL 1 DAY"));
        await ExecAsync(conn, Party(9921, "Ended", status: 4, terminatedAt: "NOW()"));

        // One link per supplier and entity; kind 1–3.
        await ExecAsync(conn, "INSERT INTO third_party_links (third_party_id, entity_id, kind, created_at) VALUES (9911, 9901, 1, NOW())");
        Assert.Contains("uq_third_party_links_third_party_id_entity_id", (await RefusedAsync(conn,
            "INSERT INTO third_party_links (third_party_id, entity_id, kind, created_at) VALUES (9911, 9901, 1, NOW())")).Message);
        await RefusedAsync(conn, "INSERT INTO third_party_links (third_party_id, entity_id, kind, created_at) VALUES (9911, 9902, 4, NOW())");

        // One location per country and purpose; purpose 1–4.
        await ExecAsync(conn, "INSERT INTO third_party_data_locations (third_party_id, country, purpose, created_at) VALUES (9911, 'BR', 1, NOW())");
        Assert.Contains("uq_third_party_data_locations_third_party_id_country_purpose", (await RefusedAsync(conn,
            "INSERT INTO third_party_data_locations (third_party_id, country, purpose, created_at) VALUES (9911, 'BR', 1, NOW())")).Message);
        await RefusedAsync(conn, "INSERT INTO third_party_data_locations (third_party_id, country, purpose, created_at) VALUES (9911, 'US', 5, NOW())");

        // One sub-processor per name and supplier.
        await ExecAsync(conn, "INSERT INTO third_party_subprocessors (third_party_id, name, subprocessor_third_party_id, processes_personal_data, created_at) VALUES (9911, 'Hyperscaler', 9912, 1, NOW())");
        Assert.Contains("uq_third_party_subprocessors_third_party_id_name", (await RefusedAsync(conn,
            "INSERT INTO third_party_subprocessors (third_party_id, name, processes_personal_data, created_at) VALUES (9911, 'hyperscaler', 0, NOW())")).Message);

        // Assessments: variant 1–4, 1–2000 questions, voided exactly with its reason; one answer per question; answer 1–4,
        // preferred 1–2, weight 1–100.
        await RefusedAsync(conn, "INSERT INTO third_party_assessments (third_party_id, variant, framework_version, expected_question_count, created_at) VALUES (9911, 5, '3.06', 10, NOW())");
        await RefusedAsync(conn, "INSERT INTO third_party_assessments (third_party_id, variant, framework_version, expected_question_count, created_at) VALUES (9911, 2, '3.06', 0, NOW())");
        await RefusedAsync(conn, "INSERT INTO third_party_assessments (third_party_id, variant, framework_version, expected_question_count, voided_at, created_at) VALUES (9911, 2, '3.06', 10, NOW(), NOW())");
        await ExecAsync(conn, "INSERT INTO third_party_assessments (id, third_party_id, variant, framework_version, expected_question_count, created_at) VALUES (9931, 9911, 2, '3.06', 10, NOW())");
        await ExecAsync(conn, "INSERT INTO third_party_assessment_answers (assessment_id, question_id, answer, preferred_answer, weight, critical, created_at) VALUES (9931, 'HFIH-01', 1, 1, 1, 0, NOW())");
        Assert.Contains("uq_third_party_assessment_answers_assessment_id_question_id", (await RefusedAsync(conn,
            "INSERT INTO third_party_assessment_answers (assessment_id, question_id, answer, weight, critical, created_at) VALUES (9931, 'HFIH-01', 2, 1, 0, NOW())")).Message);
        await RefusedAsync(conn, "INSERT INTO third_party_assessment_answers (assessment_id, question_id, answer, weight, critical, created_at) VALUES (9931, 'HFIH-02', 5, 1, 0, NOW())");
        await RefusedAsync(conn, "INSERT INTO third_party_assessment_answers (assessment_id, question_id, answer, preferred_answer, weight, critical, created_at) VALUES (9931, 'HFIH-03', 1, 3, 1, 0, NOW())");
        await RefusedAsync(conn, "INSERT INTO third_party_assessment_answers (assessment_id, question_id, answer, weight, critical, created_at) VALUES (9931, 'HFIH-04', 1, 0, 0, NOW())");

        // One SBOM per supplier and document; format 1–2.
        await ExecAsync(conn, "INSERT INTO third_party_sboms (id, third_party_id, component_name, format, document_sha256, document_size_bytes, component_count, uploaded_at) VALUES (9941, 9911, 'Moodle', 1, REPEAT('a', 64), 10, 0, NOW())");
        Assert.Contains("uq_third_party_sboms_third_party_id_document_sha256", (await RefusedAsync(conn,
            "INSERT INTO third_party_sboms (third_party_id, component_name, format, document_sha256, document_size_bytes, component_count, uploaded_at) VALUES (9911, 'Moodle', 1, REPEAT('a', 64), 10, 0, NOW())")).Message);
        await RefusedAsync(conn, "INSERT INTO third_party_sboms (third_party_id, component_name, format, document_sha256, document_size_bytes, component_count, uploaded_at) VALUES (9911, 'Moodle', 3, REPEAT('b', 64), 10, 0, NOW())");
    }

    /// <summary>
    /// Q3 — a third party in use is not deleted by the database either (links, assessments, SBOMs and a row naming it a
    /// sub-processor RESTRICT); one with only its own declarations is, and they go with it; deleting the service removes
    /// the link, deleting a user clears who wrote, deleting the unit releases the supplier filed under it.
    /// </summary>
    [Fact]
    public async Task TestQ3_InUseIsRestrictedDeclarationsCascadeReferencesRelease()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        await ExecAsync(conn, "INSERT INTO third_party_subprocessors (third_party_id, name, subprocessor_third_party_id, processes_personal_data, created_at) VALUES (9911, 'Hyperscaler', 9912, 1, NOW())");
        await RefusedAsync(conn, "DELETE FROM third_parties WHERE id = 9912");

        await ExecAsync(conn, "INSERT INTO third_party_links (third_party_id, entity_id, kind, created_at, created_by_id) VALUES (9911, 9901, 1, NOW(), 990)");
        await RefusedAsync(conn, "DELETE FROM third_parties WHERE id = 9911");

        await ExecAsync(conn, "DELETE FROM entities WHERE Id = 9901");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM third_party_links"));

        await ExecAsync(conn, "INSERT INTO third_party_assessments (id, third_party_id, variant, framework_version, expected_question_count, created_at) VALUES (9931, 9911, 2, '3.06', 10, NOW())");
        await RefusedAsync(conn, "DELETE FROM third_parties WHERE id = 9911");
        await ExecAsync(conn, "DELETE FROM third_party_assessments WHERE id = 9931");

        await ExecAsync(conn, "INSERT INTO third_party_sboms (id, third_party_id, component_name, format, document_sha256, document_size_bytes, component_count, uploaded_at) VALUES (9941, 9911, 'Moodle', 1, REPEAT('a', 64), 10, 1, NOW())");
        await ExecAsync(conn, "INSERT INTO third_party_sbom_components (sbom_id, name, created_at) VALUES (9941, 'log4j-core', NOW())");
        await RefusedAsync(conn, "DELETE FROM third_parties WHERE id = 9911");
        await ExecAsync(conn, "DELETE FROM third_party_sboms WHERE id = 9941");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM third_party_sbom_components"));

        await ExecAsync(conn, "INSERT INTO third_party_data_locations (third_party_id, country, purpose, created_at) VALUES (9911, 'BR', 1, NOW())");
        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 990");
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM third_parties WHERE id = 9911 AND owner_id IS NULL AND created_by_id IS NULL"));

        await ExecAsync(conn, "DELETE FROM third_parties WHERE id = 9911");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM third_party_subprocessors"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM third_party_data_locations"));

        await ExecAsync(conn, "DELETE FROM entities WHERE Id = 9903");
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM third_parties WHERE id = 9912 AND entity_id IS NULL"));
    }
}
