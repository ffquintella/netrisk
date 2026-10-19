using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DAL.Enums;
using DAL.Migrations;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Migrations;
using Model.RiskIndex;
using Xunit;

namespace ConsoleClient.Tests.DB;

/// <summary>
/// The T243/T293 Data script checked against the code it must agree with, without a database (S40 §12.7): permissions
/// by natural key and never by id, roles created only when absent, grants by role name and key exactly as
/// <see cref="RiskIndexRoles.SeededGrants"/> says, nothing for Administrator, settings that keep an operator's value.
/// Its behaviour on real rows is <c>DAL.IntegrationTests/RiskIndexSchemaTests</c>.
/// </summary>
[TestSubject(typeof(RiskIndexRoles))]
public class RiskIndexSeedTest
{
    private static readonly RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    /// <summary>The Data script seeding <c>risk_index_approve</c> (its VALUES row), found by content rather than by number.</summary>
    private static string SeedScript()
    {
        var matches = new DirectoryInfo(Path.Combine(RepoLayout.DbDirectory.FullName, "Data"))
            .GetFiles("*.sql")
            .Where(f => File.ReadAllText(f.FullName).Contains($"('{RiskIndexPermissions.Approve}',"))
            .ToList();

        Assert.Single(matches);
        return Regex.Replace(File.ReadAllText(matches[0].FullName), @"--[^\n]*", " ");
    }

    private static IEnumerable<string> Quoted(string list) =>
        Regex.Matches(list, @"'([^']*)'").Select(m => m.Groups[1].Value);

    [Fact]
    public void TestThePermissionsAreInsertedByKeyWithoutAnId()
    {
        var sql = SeedScript();

        var insert = Regex.Match(sql, @"INSERT\s+IGNORE\s+INTO\s+`permissions`\s*\(([^)]*)\)\s*VALUES(.*?\))\s*;[ \t]*\r?\n", Opts);
        Assert.True(insert.Success, "The permissions must be inserted with INSERT IGNORE.");
        Assert.DoesNotContain("`id`", insert.Groups[1].Value);

        var keys = Regex.Matches(insert.Groups[2].Value, @"\(\s*'([a-z_]+)'").Select(m => m.Groups[1].Value);
        Assert.Equal(new[] { RiskIndexPermissions.Approve, RiskIndexPermissions.Configure, RiskIndexPermissions.View },
            keys.Order());
    }

    [Theory]
    [InlineData(RiskIndexRoles.RiskManager)]
    [InlineData(RiskIndexRoles.RiskAdministrator)]
    public void TestEachApproverRoleIsCreatedOnlyWhenAbsentAndIsNotAnAdministratorRole(string role)
    {
        var sql = SeedScript();

        Assert.Matches(new Regex(
            $@"INSERT\s+INTO\s+`role`\s*\(`name`,\s*`admin`,\s*`default`\)\s*SELECT\s+'{role}',\s*0,\s*NULL\s+FROM\s+DUAL\s+" +
            $@"WHERE\s+NOT\s+EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+`role`\s+WHERE\s+`name`\s*=\s*'{role}'\s*\)\s*;", Opts), sql);
    }

    /// <summary>Each grant statement joins roles by name and permissions by key; together they are exactly the grant table.</summary>
    [Fact]
    public void TestTheGrantsAreExactlyTheSeededGrantTable()
    {
        var sql = SeedScript();

        var grants = Regex.Matches(sql,
                @"INSERT\s+IGNORE\s+INTO\s+`role_responsibilities`\s*\(`role_id`,\s*`permission_id`\)\s*" +
                @"SELECT\s+r\.`value`,\s*p\.`id`\s+FROM\s+`role`\s+r\s+JOIN\s+`permissions`\s+p\s+ON\s+p\.`key`\s+IN\s*\(([^)]*)\)\s+" +
                @"WHERE\s+r\.`name`\s+(?:IN\s*\(([^)]*)\)|=\s*('[^']*'))\s*;", Opts)
            .SelectMany(m => Quoted(m.Groups[2].Success && m.Groups[2].Length > 0 ? m.Groups[2].Value : m.Groups[3].Value)
                .SelectMany(role => Quoted(m.Groups[1].Value).Select(key => (role, key))))
            .ToHashSet();

        var expected = RiskIndexRoles.SeededGrants
            .SelectMany(g => g.Value.Select(key => (role: g.Key, key)))
            .ToHashSet();

        Assert.Equal(expected.OrderBy(g => g.role).ThenBy(g => g.key), grants.OrderBy(g => g.role).ThenBy(g => g.key));
        Assert.Equal(2, Regex.Matches(sql, @"INSERT\s+IGNORE\s+INTO\s+`role_responsibilities`", Opts).Count);
    }

    /// <summary>The administrator flag never approves weights (S40 §11.4); the seed names neither it nor the third line.</summary>
    [Theory]
    [InlineData(RiskIndexRoles.Administrator)]
    [InlineData("ThirdLineAuditor")]
    public void TestNoIndexPermissionIsGrantedToTheAdministratorOrTheThirdLine(string role)
    {
        Assert.DoesNotContain($"'{role}'", SeedScript());
    }

    [Fact]
    public void TestNoGrantNamesALiteralId()
    {
        Assert.DoesNotMatch(new Regex(@"`role_responsibilities`[^;]*VALUES", Opts), SeedScript());
    }

    [Theory]
    [InlineData(RiskIndexSettingKeys.ApproverRoles, RiskIndexRoles.DefaultApproverRoles)]
    [InlineData(RiskIndexSettingKeys.Publication, RiskIndexSettingKeys.PublicationShadow)]
    public void TestEachSettingIsSeededWithoutOverwritingAnOperatorsValue(string name, string value)
    {
        Assert.Matches(new Regex(
            $@"INSERT\s+INTO\s+`settings`\s*\(`name`,\s*`value`\)\s*VALUES\s*\(\s*'{name}',\s*'{Regex.Escape(value)}'\s*\)\s*" +
            @"ON\s+DUPLICATE\s+KEY\s+UPDATE\s+`value`\s*=\s*`value`\s*;", Opts), SeedScript());
    }

    /// <summary>The history row names the migration that carries the tables, so the EF history and the model agree.</summary>
    [Fact]
    public void TestTheHistoryRowNamesTheRiskIndexMigration()
    {
        var id = typeof(RiskIndexTables).GetCustomAttribute<MigrationAttribute>()!.Id;

        Assert.Matches(new Regex(
            $@"INSERT\s+INTO\s+`__EFMigrationsHistory`[^;]*'{id}'[^;]*ON\s+DUPLICATE\s+KEY\s+UPDATE", Opts), SeedScript());
    }
}
