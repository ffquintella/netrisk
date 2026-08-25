using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConsoleClient.Models;
using JetBrains.Annotations;
using Xunit;

namespace ConsoleClient.Tests.DB;

/// <summary>
/// Replays <c>DB/Structure/{n}.sql</c> in the order <c>DatabaseService.Update()</c> applies them,
/// tracking which tables exist at each step, and fails if a script touches a table that does not.
///
/// The failure this guards against is silent at authoring time and fatal at upgrade time: the
/// numbered SQL is split by hand out of an EF migration, and a table whose name drifted from the
/// EF one (<c>files</c> was renamed to <c>nr_files</c> back in Structure/3.sql) parses fine,
/// reviews fine, and then aborts a production upgrade halfway through a script — after the earlier
/// DDL in that same script has already implicitly committed, leaving the database between versions.
/// </summary>
[TestSubject(typeof(DatabaseInformation))]
public class SchemaUpgradeTableReferencesTest
{
    // Backtick-quoted or bare identifier.
    private const string Identifier = @"(?:`[^`]+`|\w+)";

    private static readonly RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    [Fact]
    public void TestEveryStructureScriptOnlyTouchesTablesThatExistAtThatVersion()
    {
        var directory = new DirectoryInfo(Path.Combine(RepoLayout.DbDirectory.FullName, "Structure"));

        var scripts = directory.GetFiles("*.sql")
            .Where(f => int.TryParse(Path.GetFileNameWithoutExtension(f.Name), out _))
            .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f.Name)))
            .ToList();

        var existing = new HashSet<string>();
        var violations = new List<string>();

        foreach (var script in scripts)
        {
            var sql = StripCommentsAndLiterals(File.ReadAllText(script.FullName));

            // A script's own CREATE TABLEs count as visible for the whole script: the base install
            // (1.sql) is a dump whose foreign keys legitimately point forward at tables created
            // further down the same file.
            var visible = new HashSet<string>(existing);
            foreach (Match match in Regex.Matches(sql,
                         $@"create\s+table\s+(?:if\s+not\s+exists\s+)?({Identifier}(?:\.{Identifier})?)", Options))
            {
                visible.Add(Normalize(match.Groups[1].Value));
            }

            foreach (var statement in sql.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(statement)) continue;

                if (TryApplyDrop(statement, existing, visible)) continue;
                if (TryApplyRename(statement, existing, visible)) continue;

                // Re-add in statement order: the base install drops a table before creating it, and
                // the pre-seeding above must not leave that drop as the last word.
                TryApplyCreate(statement, existing, visible);

                foreach (Match match in Regex.Matches(statement,
                             $@"(?:alter\s+table|create\s+(?:unique\s+|fulltext\s+)?index\s+{Identifier}\s+on|references)" +
                             $@"\s+({Identifier}(?:\.{Identifier})?)", Options))
                {
                    var table = Normalize(match.Groups[1].Value);
                    if (visible.Contains(table)) continue;

                    violations.Add($"Structure/{script.Name}: `{table}` does not exist at this version " +
                                   $"— {Excerpt(statement)}");
                }

                TryApplyAlterTableRename(statement, existing, visible);
            }

            existing.UnionWith(visible);
        }

        Assert.True(violations.Count == 0,
            "These upgrade scripts reference tables that do not exist when they run, so " +
            "DatabaseService.Update() will abort mid-script and strand the database between versions:" +
            $"{string.Join("", violations.Select(v => "\n  " + v))}");
    }

    /// <summary>CREATE TABLE brings the table into scope from this statement onwards.</summary>
    private static void TryApplyCreate(string statement, HashSet<string> existing, HashSet<string> visible)
    {
        var match = Regex.Match(statement.TrimStart(),
            $@"^create\s+table\s+(?:if\s+not\s+exists\s+)?({Identifier}(?:\.{Identifier})?)", Options);

        if (match.Success) Remember(Normalize(match.Groups[1].Value), existing, visible);
    }

    /// <summary>DROP TABLE removes the table from scope for every later script.</summary>
    private static bool TryApplyDrop(string statement, HashSet<string> existing, HashSet<string> visible)
    {
        var match = Regex.Match(statement.TrimStart(), $@"^drop\s+table\s+(?:if\s+exists\s+)?(.+)$", Options);
        if (!match.Success) return false;

        foreach (var table in match.Groups[1].Value.Split(','))
        {
            Forget(Normalize(table), existing, visible);
        }

        return true;
    }

    /// <summary>RENAME TABLE a TO b — the reason a stale name survives review at all.</summary>
    private static bool TryApplyRename(string statement, HashSet<string> existing, HashSet<string> visible)
    {
        var match = Regex.Match(statement.TrimStart(), $@"^rename\s+table\s+(.+)$", Options);
        if (!match.Success) return false;

        foreach (var pair in match.Groups[1].Value.Split(','))
        {
            var renamed = Regex.Match(pair, $@"^\s*({Identifier})\s+to\s+({Identifier})", Options);
            if (!renamed.Success) continue;

            Forget(Normalize(renamed.Groups[1].Value), existing, visible);
            Remember(Normalize(renamed.Groups[2].Value), existing, visible);
        }

        return true;
    }

    /// <summary>ALTER TABLE a RENAME [TO] b — RENAME INDEX/COLUMN/KEY are not table renames.</summary>
    private static void TryApplyAlterTableRename(string statement, HashSet<string> existing, HashSet<string> visible)
    {
        var altered = Regex.Match(statement.TrimStart(), $@"^alter\s+table\s+({Identifier})", Options);
        if (!altered.Success) return;

        foreach (Match match in Regex.Matches(statement,
                     $@"\brename\s+(?!index\b|column\b|key\b)(?:to\s+)?({Identifier})", Options))
        {
            Forget(Normalize(altered.Groups[1].Value), existing, visible);
            Remember(Normalize(match.Groups[1].Value), existing, visible);
        }
    }

    private static void Forget(string table, HashSet<string> existing, HashSet<string> visible)
    {
        existing.Remove(table);
        visible.Remove(table);
    }

    private static void Remember(string table, HashSet<string> existing, HashSet<string> visible)
    {
        existing.Add(table);
        visible.Add(table);
    }

    /// <summary>MySQL table names are compared case-insensitively here, and the schema qualifier
    /// (<c>netrisk.entities</c>) is dropped — every script runs against the connection's schema.</summary>
    private static string Normalize(string table)
    {
        var name = table.Trim().Trim('`').ToLowerInvariant();
        var separator = name.LastIndexOf('.');

        return (separator < 0 ? name : name[(separator + 1)..]).Trim('`');
    }


    /// <summary>
    /// Replaces each <c>SET @nr_ddl = IF(<em>probe</em>, 'A', 'B'); PREPARE … EXECUTE … DEALLOCATE …</c>
    /// block with the DDL it carries, so the replay sees the renames those blocks perform. Without
    /// this the conditional half of every rename would be stripped away as a string literal, and a
    /// table that only ever comes into existence by being renamed would look like it never exists.
    /// </summary>
    private static string UnwrapGuards(string sql)
    {
        sql = Regex.Replace(sql,
            @"set\s+@nr_ddl\s*=\s*IF\s*\(.*?,\s*'((?:[^']|'')*)'\s*,\s*'((?:[^']|'')*)'\s*\)",
            match =>
            {
                var first = match.Groups[1].Value.Replace("''", "'");
                var second = match.Groups[2].Value.Replace("''", "'");
                var ddl = first.Trim().Equals("DO 0", StringComparison.OrdinalIgnoreCase) ? second : first;

                return ddl.Trim().Equals("DO 0", StringComparison.OrdinalIgnoreCase) ? "DO 0" : ddl;
            },
            Options);

        return Regex.Replace(sql, @"(prepare\s+nr_ddl\s+from\s+@nr_ddl|execute\s+nr_ddl|deallocate\s+prepare\s+nr_ddl)",
            "DO 0", Options);
    }

    /// <summary>Comments and string literals can hold anything that looks like SQL, including the
    /// very table names these scripts explain in prose.</summary>
    private static string StripCommentsAndLiterals(string sql)
    {
        sql = UnwrapGuards(sql);
        sql = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        sql = Regex.Replace(sql, @"--[^\n]*", " ");
        sql = Regex.Replace(sql, @"^\s*#[^\n]*", " ", RegexOptions.Multiline);
        sql = Regex.Replace(sql, @"'(?:[^'\\]|\\.|'')*'", "''");

        return sql;
    }

    private static string Excerpt(string statement)
    {
        var collapsed = Regex.Replace(statement.Trim(), @"\s+", " ");

        return collapsed.Length <= 90 ? collapsed : collapsed[..90] + "…";
    }
}
