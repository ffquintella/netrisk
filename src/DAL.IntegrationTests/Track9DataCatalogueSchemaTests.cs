using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.11 (S52 §4, §8 Q1–Q3) — the LGPD data catalogue version on a real MariaDB: the seven tables arrive empty, the
/// write permission is seeded once and granted to nobody, and a retry converges without a second row; the CHECKs and unique
/// indexes refuse what the services refuse — one requirement per code (case-insensitively), one catalogue entry per data
/// record, one purpose per entry (case- and accent-insensitively), one location per country and purpose, a RIPD that is
/// approved only with its date and retired only with its date and reason —, which the in-memory provider cannot enforce; and
/// a requirement in use (cited by a purpose, a retention or a risk) cannot be deleted (RESTRICT), while a data record takes
/// its catalogue and its RIPD links with it and a deleted risk or user releases what pointed at it.
///
/// The version is found by its marker, never written here (S42 §11, defect 1). Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9DataCatalogueSchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`risk_legal_requirements`");

    private static readonly string[] Tables =
    [
        "legal_requirements", "data_catalogue_entries", "data_catalogue_purposes", "data_catalogue_locations",
        "dpias", "dpia_links", "risk_legal_requirements"
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

    /// <summary>
    /// User 990; data records 9901, 9903 and 9904 (<c>organizationData</c>) and process 9902 (<c>businessProcess</c>); third
    /// party 9911; risk 9921; requirement 9931 (<c>LGPD-7</c>); and a catalogue entry 9941 for data record 9901.
    /// </summary>
    private static Task SeedAsync(MySqlConnection conn) => ExecAsync(conn,
        "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
        "VALUES (990,1,0,'local','U990','u990@x.test','s',REPEAT('x',60),1,0,'user990');" +
        "INSERT INTO entities (Id, DefinitionName, DefinitionVersion, Created, Updated, CreatedBy, UpdatedBy, Status, Parent) VALUES " +
        "(9901,'organizationData','2.6',NOW(),NOW(),990,990,'active',NULL),(9902,'businessProcess','2.6',NOW(),NOW(),990,990,'active',NULL)," +
        "(9903,'organizationData','2.6',NOW(),NOW(),990,990,'active',NULL),(9904,'organizationData','2.6',NOW(),NOW(),990,990,'active',NULL);" +
        "INSERT INTO third_parties (id, name, status, is_cloud_provider, is_identity_provider, created_at) " +
        "VALUES (9911, 'Acme SaaS', 2, 0, 0, NOW());" +
        "INSERT INTO `risks` (`id`,`status`) VALUES (9921,'New');" +
        "INSERT INTO legal_requirements (id, code, title, kind, created_at) VALUES (9931, 'LGPD-7', 'LGPD art. 7', 1, NOW());" +
        "INSERT INTO data_catalogue_entries (id, entity_id, large_volume, strategic_research, created_at) VALUES (9941, 9901, 0, 0, NOW());");

    private static string Requirement(int id, string code, int kind = 1) =>
        $"INSERT INTO legal_requirements (id, code, title, kind, created_at) VALUES ({id}, '{code}', 'Title {code}', {kind}, NOW())";

    private static string Entry(int id, int entityId, string extra = "", string values = "") =>
        $"INSERT INTO data_catalogue_entries (id, entity_id, large_volume, strategic_research, created_at{extra}) " +
        $"VALUES ({id}, {entityId}, 0, 0, NOW(){values})";

    private static string Purpose(string purpose, string extra = "", string values = "", int entryId = 9941) =>
        $"INSERT INTO data_catalogue_purposes (entry_id, purpose, created_at{extra}) VALUES ({entryId}, '{purpose}', NOW(){values})";

    private static string Location(string country, int purpose, int entryId = 9941) =>
        $"INSERT INTO data_catalogue_locations (entry_id, country, purpose, created_at) VALUES ({entryId}, '{country}', {purpose}, NOW())";

    private static string Dpia(int id, int status, string extra = "", string values = "") =>
        $"INSERT INTO dpias (id, title, status, created_at{extra}) VALUES ({id}, 'RIPD {id}', {status}, NOW(){values})";

    /// <summary>
    /// Q1 — the upgrade creates the seven tables empty, seeds <c>data_catalogue_manage</c> once and grants it to nobody, and a
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

        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM permissions WHERE `key` = 'data_catalogue_manage'"));
        Assert.Equal(0L, await CountAsync(conn,
            "SELECT COUNT(*) FROM role_responsibilities rr JOIN permissions p ON p.id = rr.permission_id WHERE p.`key` = 'data_catalogue_manage'"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_Track9DataCatalogue'"));
    }

    /// <summary>Q2 — the CHECKs and unique indexes refuse what the services refuse.</summary>
    [Fact]
    public async Task TestQ2_ConstraintsRefuseWhatTheServicesRefuse()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        // One row per requirement code, case-insensitively (the collation); kind 1–4.
        Assert.Contains("uq_legal_requirements_code", (await RefusedAsync(conn, Requirement(9932, "lgpd-7"))).Message);
        await RefusedAsync(conn, Requirement(9933, "KIND-5", kind: 5));
        await RefusedAsync(conn, Requirement(9934, "KIND-0", kind: 0));
        await ExecAsync(conn, Requirement(9935, "NORM-1", kind: 4));

        // Personal data 1–4 or NULL; retention 0–1200 months; transfer mechanism 1–12; one entry per data record.
        await ExecAsync(conn, Entry(9942, 9904, ", personal_data, retention_period_months, transfer_mechanism", ", 4, 1200, 12"));
        await RefusedAsync(conn, Entry(9943, 9903, ", personal_data", ", 5"));
        await RefusedAsync(conn, Entry(9943, 9903, ", retention_period_months", ", -1"));
        await RefusedAsync(conn, Entry(9943, 9903, ", retention_period_months", ", 1201"));
        await RefusedAsync(conn, Entry(9943, 9903, ", transfer_mechanism", ", 13"));
        await RefusedAsync(conn, Entry(9943, 9903, ", transfer_mechanism", ", 0"));
        Assert.Contains("uq_data_catalogue_entries_entity_id", (await RefusedAsync(conn, Entry(9944, 9901))).Message);

        // Legal basis 1–18 or NULL; one purpose per entry, whatever the case or the accent.
        await RefusedAsync(conn, Purpose("Basis nineteen", ", legal_basis", ", 19"));
        await ExecAsync(conn, Purpose("Matrícula", ", legal_basis", ", 18"));
        Assert.Contains("uq_data_catalogue_purposes_entry_id_purpose",
            (await RefusedAsync(conn, Purpose("matricula"))).Message);
        await ExecAsync(conn, Purpose("Matrícula", entryId: 9942));

        // One location per country and purpose; purpose 1–4.
        await ExecAsync(conn, Location("BR", 1));
        Assert.Contains("uq_data_catalogue_locations_entry_id_country_purpose",
            (await RefusedAsync(conn, Location("BR", 1))).Message);
        await RefusedAsync(conn, Location("US", 5));
        await RefusedAsync(conn, Location("US", 0));
        await ExecAsync(conn, Location("BR", 3));

        // A RIPD: status 1–3; approved only with its date; retired exactly with its date and reason; residual risk 1–3.
        await ExecAsync(conn, Dpia(9961, 1));
        await RefusedAsync(conn, Dpia(9962, 4));
        await RefusedAsync(conn, Dpia(9962, 2));
        await ExecAsync(conn, Dpia(9963, 2, ", approved_at", ", NOW()"));
        await RefusedAsync(conn, Dpia(9964, 3));
        await RefusedAsync(conn, Dpia(9964, 3, ", retired_at", ", NOW()"));
        await RefusedAsync(conn, Dpia(9964, 3, ", retire_reason", ", 'Superseded by the next review.'"));
        await ExecAsync(conn, Dpia(9965, 3, ", retired_at, retire_reason", ", NOW(), 'Superseded by the next review.'"));
        await RefusedAsync(conn, Dpia(9966, 1, ", retired_at", ", NOW()"));
        await RefusedAsync(conn, Dpia(9966, 2, ", approved_at, retired_at", ", NOW(), NOW()"));
        await RefusedAsync(conn, Dpia(9966, 1, ", residual_risk", ", 4"));
        await ExecAsync(conn, Dpia(9966, 1, ", residual_risk", ", 3"));

        // One link per RIPD and entity; kind 1–2.
        await ExecAsync(conn, "INSERT INTO dpia_links (dpia_id, entity_id, kind, created_at) VALUES (9961, 9901, 1, NOW())");
        Assert.Contains("uq_dpia_links_dpia_id_entity_id", (await RefusedAsync(conn,
            "INSERT INTO dpia_links (dpia_id, entity_id, kind, created_at) VALUES (9961, 9901, 1, NOW())")).Message);
        await RefusedAsync(conn, "INSERT INTO dpia_links (dpia_id, entity_id, kind, created_at) VALUES (9961, 9902, 3, NOW())");
        await ExecAsync(conn, "INSERT INTO dpia_links (dpia_id, entity_id, kind, created_at) VALUES (9961, 9902, 2, NOW())");

        // One link per risk and requirement.
        await ExecAsync(conn, "INSERT INTO risk_legal_requirements (risk_id, legal_requirement_id, note, created_at) VALUES (9921, 9931, 'Cited by the DPO', NOW())");
        Assert.Contains("uq_risk_legal_requirements_risk_id_legal_requirement_id", (await RefusedAsync(conn,
            "INSERT INTO risk_legal_requirements (risk_id, legal_requirement_id, created_at) VALUES (9921, 9931, NOW())")).Message);
    }

    /// <summary>
    /// Q3 — a requirement in use is not deleted by the database either (a purpose, a retention and a risk link each RESTRICT,
    /// then release it and it goes); a third party named by a contract is not deleted; deleting the data record takes its
    /// catalogue, purposes, locations and RIPD links with it; deleting the risk takes its requirement links; deleting the
    /// user clears who wrote and approved.
    /// </summary>
    [Fact]
    public async Task TestQ3_InUseIsRestrictedTheCatalogueCascadesReferencesRelease()
    {
        await fixture.InitializeNumberedSchemaAsync(V);
        await using var conn = await OpenAsync();
        await SeedAsync(conn);

        // Cited by a purpose.
        await ExecAsync(conn, Requirement(9932, "BY-PURPOSE"));
        await ExecAsync(conn, Purpose("Enrolment", ", legal_basis, legal_requirement_id", ", 2, 9932"));
        await RefusedAsync(conn, "DELETE FROM legal_requirements WHERE id = 9932");
        await ExecAsync(conn, "DELETE FROM data_catalogue_purposes WHERE legal_requirement_id = 9932");
        await ExecAsync(conn, "DELETE FROM legal_requirements WHERE id = 9932");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM legal_requirements WHERE id = 9932"));

        // Cited by a retention.
        await ExecAsync(conn, Requirement(9933, "BY-RETENTION"));
        await ExecAsync(conn, "UPDATE data_catalogue_entries SET retention_requirement_id = 9933 WHERE id = 9941");
        await RefusedAsync(conn, "DELETE FROM legal_requirements WHERE id = 9933");
        await ExecAsync(conn, "UPDATE data_catalogue_entries SET retention_requirement_id = NULL WHERE id = 9941");
        await ExecAsync(conn, "DELETE FROM legal_requirements WHERE id = 9933");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM legal_requirements WHERE id = 9933"));

        // Linked to a risk.
        await ExecAsync(conn, Requirement(9934, "BY-RISK"));
        await ExecAsync(conn, "INSERT INTO risk_legal_requirements (risk_id, legal_requirement_id, created_at) VALUES (9921, 9934, NOW())");
        await RefusedAsync(conn, "DELETE FROM legal_requirements WHERE id = 9934");
        await ExecAsync(conn, "DELETE FROM risk_legal_requirements WHERE legal_requirement_id = 9934");
        await ExecAsync(conn, "DELETE FROM legal_requirements WHERE id = 9934");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM legal_requirements WHERE id = 9934"));

        // A third party named as the counterparty of a contract.
        await ExecAsync(conn, "INSERT INTO legal_requirements (id, code, title, kind, third_party_id, created_at) VALUES (9935, 'CONTRACT-1', 'DPA with Acme', 3, 9911, NOW())");
        await RefusedAsync(conn, "DELETE FROM third_parties WHERE id = 9911");
        await ExecAsync(conn, "DELETE FROM legal_requirements WHERE id = 9935");
        await ExecAsync(conn, "DELETE FROM third_parties WHERE id = 9911");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM third_parties WHERE id = 9911"));

        // Deleting the data record removes its catalogue, purposes and locations, and its RIPD links; the RIPD and the link
        // to the process stay.
        await ExecAsync(conn, Purpose("Enrolment", ", legal_basis", ", 2"));
        await ExecAsync(conn, Location("BR", 1));
        await ExecAsync(conn, Dpia(9961, 1));
        await ExecAsync(conn, "INSERT INTO dpia_links (dpia_id, entity_id, kind, created_at) VALUES (9961, 9901, 1, NOW()), (9961, 9902, 2, NOW())");
        await ExecAsync(conn, "DELETE FROM entities WHERE Id = 9901");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM data_catalogue_entries WHERE entity_id = 9901"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM data_catalogue_purposes"));
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM data_catalogue_locations"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM dpia_links"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM dpia_links WHERE entity_id = 9902"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM dpias WHERE id = 9961"));

        // Deleting the risk removes its requirement link; the requirement stays.
        await ExecAsync(conn, "INSERT INTO risk_legal_requirements (risk_id, legal_requirement_id, created_at) VALUES (9921, 9931, NOW())");
        await ExecAsync(conn, "DELETE FROM `risks` WHERE id = 9921");
        Assert.Equal(0L, await CountAsync(conn, "SELECT COUNT(*) FROM risk_legal_requirements"));
        Assert.Equal(1L, await CountAsync(conn, "SELECT COUNT(*) FROM legal_requirements WHERE id = 9931"));

        // Deleting the user clears who wrote and who approved, and deletes nothing.
        await ExecAsync(conn, "UPDATE legal_requirements SET created_by_id = 990, updated_by_id = 990 WHERE id = 9931");
        await ExecAsync(conn, Entry(9942, 9903, ", created_by_id, updated_by_id", ", 990, 990"));
        await ExecAsync(conn, Dpia(9963, 2, ", approved_at, approved_by_id, created_by_id, updated_by_id", ", NOW(), 990, 990, 990"));
        await ExecAsync(conn, "DELETE FROM `user` WHERE `value` = 990");
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM legal_requirements WHERE id = 9931 AND created_by_id IS NULL AND updated_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM data_catalogue_entries WHERE id = 9942 AND created_by_id IS NULL AND updated_by_id IS NULL"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM dpias WHERE id = 9963 AND status = 2 AND approved_by_id IS NULL AND created_by_id IS NULL AND updated_by_id IS NULL"));
    }
}
