using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// GitHub #80 (T297, S44) — <c>assessment_run_answers.comment</c> and <c>nr_files.assessment_run_answer_id</c>
/// on a real MariaDB: the upgrade leaves every existing answer without a comment, a retry converges,
/// deleting an answer cascades to its evidence, and the EF model round-trips both columns.
///
/// The version is found by its marker rather than written here, so the tests survive a renumbering on
/// merge and a later version landing on top.
///
/// The run and question an answer hangs off are planted with foreign-key checks off: the entity,
/// assessment and user rows a real run needs are irrelevant to what is under test here, and the cascade
/// from the answer to its files fires the same either way.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class AssessmentAnswerEvidenceSchemaTests(MariaDbContainerFixture fixture)
{
    private static int V => MariaDbContainerFixture.VersionIntroducing("`assessment_run_answer_id`");

    private static int N => MariaDbContainerFixture.TargetSchemaVersion;

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

    private static async Task ApplyVersionAsync(MySqlConnection conn, int version, params string[] parts)
    {
        foreach (var part in parts.Length == 0 ? ["Structure", "Data"] : parts)
            await ExecAsync(conn, await File.ReadAllTextAsync(
                Path.Combine(MariaDbContainerFixture.RepoDbDir(), part, $"{version}.sql")));
    }

    private static Task SeedAnswerAsync(MySqlConnection conn, int id = 1) => ExecAsync(conn,
        "SET FOREIGN_KEY_CHECKS = 0; " +
        "INSERT INTO `assessment_run_answers` (`id`,`assessment_run_id`,`assessment_question_id`,`answer_content_json`) " +
        $"VALUES ({id}, 1, 1, '\"Yes\"'); " +
        "SET FOREIGN_KEY_CHECKS = 1;");

    private static Task SeedEvidenceAsync(MySqlConnection conn, int fileId, int answerId) => ExecAsync(conn,
        "INSERT INTO `nr_files` (`id`,`name`,`unique_name`,`type`,`size`,`user`,`content`,`assessment_run_answer_id`) " +
        $"VALUES ({fileId}, 'photo.png', 'u-{fileId}', '3', 3, 1, x'010203', {answerId});");

    /// <summary>The upgrade adds both columns nullable and gives no existing answer a comment.</summary>
    [Fact]
    public async Task TestTheUpgradeLeavesExistingAnswersWithoutAComment()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();
        await SeedAnswerAsync(conn);

        await ApplyVersionAsync(conn, V);

        Assert.Equal(V.ToString(), await ScalarAsync(conn, "SELECT value FROM settings WHERE name = 'db_version'"));
        Assert.Null(await ScalarAsync(conn, "SELECT comment FROM assessment_run_answers WHERE id = 1"));
        Assert.Equal("\"Yes\"", await ScalarAsync(conn, "SELECT answer_content_json FROM assessment_run_answers WHERE id = 1"));
        Assert.Equal("YES", await ScalarAsync(conn,
            "SELECT IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND TABLE_NAME = 'nr_files' AND COLUMN_NAME = 'assessment_run_answer_id'"));
        Assert.Equal("text", await ScalarAsync(conn,
            "SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND TABLE_NAME = 'assessment_run_answers' AND COLUMN_NAME = 'comment'"));
    }

    /// <summary>
    /// A retry converges: Structure applied twice more (with db_version back at v − 1, as after a failed
    /// Data script) and Data again leave one column, one index, one constraint and one history row.
    /// </summary>
    [Fact]
    public async Task TestReplayingTheVersionConverges()
    {
        await fixture.InitializeNumberedSchemaAsync(V - 1);
        await using var conn = await OpenAsync();

        await ApplyVersionAsync(conn, V);
        await ExecAsync(conn, $"UPDATE settings SET value = '{V - 1}' WHERE name = 'db_version';");
        await ApplyVersionAsync(conn, V, "Structure");
        await ApplyVersionAsync(conn, V, "Structure");
        await ApplyVersionAsync(conn, V, "Data");

        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(conn,
            "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId LIKE '%_AssessmentAnswerEvidence'")));
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND TABLE_NAME = 'assessment_run_answers' AND COLUMN_NAME = 'comment'")));
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(conn,
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND TABLE_NAME = 'nr_files' AND INDEX_NAME = 'idx_nr_files_assessment_run_answer_id'")));
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(conn,
            "SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() " +
            "AND CONSTRAINT_NAME = 'fk_nr_files_assessment_run_answer_id'")));
        Assert.Equal(V.ToString(), await ScalarAsync(conn, "SELECT value FROM settings WHERE name = 'db_version'"));
    }

    /// <summary>Deleting an answer takes its evidence with it, and only its own.</summary>
    [Fact]
    public async Task TestDeletingAnAnswerCascadesToItsEvidence()
    {
        await fixture.InitializeNumberedSchemaAsync(N);
        await using var conn = await OpenAsync();
        await SeedAnswerAsync(conn, 1);
        await SeedAnswerAsync(conn, 2);
        await SeedEvidenceAsync(conn, 101, 1);
        await SeedEvidenceAsync(conn, 102, 2);

        await ExecAsync(conn, "DELETE FROM assessment_run_answers WHERE id = 1;");

        Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(conn, "SELECT COUNT(*) FROM nr_files WHERE id = 101")));
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(conn, "SELECT COUNT(*) FROM nr_files WHERE id = 102")));
    }

    /// <summary>The EF model maps both columns onto the shipped DDL.</summary>
    [Fact]
    public async Task TestTheModelRoundTripsTheColumns()
    {
        await fixture.InitializeNumberedSchemaAsync(N);
        await using (var conn = await OpenAsync())
        {
            await SeedAnswerAsync(conn, 1);
        }

        await using (var write = fixture.NewContext())
        {
            var answer = await write.AssessmentRunAnswers.IgnoreQueryFilters().SingleAsync(a => a.Id == 1);
            answer.Comment = "Checked the server room badge log.";
            write.NrFiles.Add(new NrFile
            {
                Name = "badge-log.pdf", UniqueName = "u-200", Type = "19", Size = 3, User = 1,
                Content = [1, 2, 3], Timestamp = DateTime.UtcNow, AssessmentRunAnswerId = 1
            });
            await write.SaveChangesAsync();
        }

        await using (var conn = await OpenAsync())
        {
            Assert.Equal("Checked the server room badge log.",
                await ScalarAsync(conn, "SELECT comment FROM assessment_run_answers WHERE id = 1"));
            Assert.Equal(1, Convert.ToInt32(await ScalarAsync(conn,
                "SELECT assessment_run_answer_id FROM nr_files WHERE unique_name = 'u-200'")));
        }

        await using var read = fixture.NewContext();
        var files = await read.NrFiles.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.AssessmentRunAnswerId == 1).ToListAsync();
        Assert.Equal("badge-log.pdf", Assert.Single(files).Name);
    }
}
