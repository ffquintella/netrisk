using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConsoleClient.Models;
using JetBrains.Annotations;
using Xunit;

namespace ConsoleClient.Tests.DB;

/// <summary>
/// Enforces the contract that makes a numbered upgrade safe to retry.
///
/// MariaDB implicitly commits every DDL statement, so a <c>START TRANSACTION</c> around a Structure
/// script rolls nothing back — a failure part-way through leaves the database between versions with
/// <c>db_version</c> still naming the previous one. The contract is therefore split in two:
/// Structure scripts carry no transaction and every statement is guarded, so applying the version
/// again converges; Data scripts are pure DML inside a real transaction, so a failure there rolls
/// back whole and the <c>db_version</c> bump is the genuine commit point.
///
/// Both halves are hand-written per version, and a missed guard only shows up against a customer's
/// database. That is what this test exists to prevent.
/// </summary>
[TestSubject(typeof(DatabaseInformation))]
public class SchemaUpgradeIdempotenceTest
{
    private const string Identifier = @"(?:`[^`]+`|\w+)";
    private static readonly RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    private static IEnumerable<(int Version, string Sql)> Scripts(string subdirectory)
    {
        var directory = new DirectoryInfo(Path.Combine(RepoLayout.DbDirectory.FullName, subdirectory));

        return directory.GetFiles("*.sql")
            .Where(f => int.TryParse(Path.GetFileNameWithoutExtension(f.Name), out _))
            .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f.Name)))
            .Select(f => (int.Parse(Path.GetFileNameWithoutExtension(f.Name)), File.ReadAllText(f.FullName)));
    }

    /// <summary>Comments and string literals are stripped, which also hides the DDL that a guard
    /// block carries as a quoted string — exactly right, since that DDL is conditional.</summary>
    private static IEnumerable<string> StatementsOf(string sql)
    {
        sql = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        sql = Regex.Replace(sql, @"--[^\n]*", " ");
        sql = Regex.Replace(sql, @"^\s*#[^\n]*", " ", RegexOptions.Multiline);
        sql = Regex.Replace(sql, @"'(?:[^'\\]|\\.|'')*'", "''");

        return sql.Split(';').Select(s => Regex.Replace(s.Trim(), @"\s+", " ")).Where(s => s.Length > 0);
    }

    [Fact]
    public void TestStructureScriptsCarryNoTransaction()
    {
        var offenders = Scripts("Structure")
            .Where(s => Regex.IsMatch(s.Sql, @"\b(start\s+transaction|begin\s*;|commit)\b", Opts))
            .Select(s => $"Structure/{s.Version}.sql")
            .ToList();

        Assert.True(offenders.Count == 0,
            "A transaction around DDL is a promise MariaDB cannot keep — it implicitly commits each " +
            "statement, so these scripts would still half-apply on failure while reading as atomic: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void TestEveryDataScriptIsTransactional()
    {
        var offenders = new List<string>();

        foreach (var (version, sql) in Scripts("Data"))
        {
            if (string.IsNullOrWhiteSpace(sql)) continue;
            if (!Regex.IsMatch(sql, @"\bstart\s+transaction\b", Opts) || !Regex.IsMatch(sql, @"\bcommit\b", Opts))
                offenders.Add($"Data/{version}.sql");
        }

        Assert.True(offenders.Count == 0,
            "Data scripts are pure DML, so a transaction there is real and is what makes the version " +
            $"bump all-or-nothing. These have no wrapper: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void TestDataScriptsContainNoDdl()
    {
        var offenders = new List<string>();

        foreach (var (version, sql) in Scripts("Data"))
        foreach (var statement in StatementsOf(sql))
        {
            if (Regex.IsMatch(statement, @"^(create|alter|drop|rename|truncate)\b", Opts))
                offenders.Add($"Data/{version}.sql: {Excerpt(statement)}");
        }

        Assert.True(offenders.Count == 0,
            "DDL in a Data script silently commits the transaction around it, so the rollback that " +
            $"script relies on stops working:{string.Join("", offenders.Select(o => "\n  " + o))}");
    }

    [Fact]
    public void TestEveryStructureStatementIsSafeToApplyTwice()
    {
        var offenders = new List<string>();

        foreach (var (version, sql) in Scripts("Structure"))
        foreach (var statement in StatementsOf(sql))
        {
            var problem = UnguardedReason(statement);
            if (problem != null) offenders.Add($"Structure/{version}.sql: {problem} — {Excerpt(statement)}");
        }

        Assert.True(offenders.Count == 0,
            "These statements fail when the version they belong to is applied a second time, so a " +
            "part-applied upgrade cannot be retried and has to be repaired by hand:" +
            string.Join("", offenders.Select(o => "\n  " + o)));
    }

    /// <summary>Returns why a statement would not survive a second application, or null if it would.</summary>
    private static string? UnguardedReason(string statement)
    {
        // A guard block: SET @nr_ddl = IF(<probe>, …) / PREPARE / EXECUTE / DEALLOCATE. The conditional
        // DDL rode in as a string literal and was stripped above, so there is nothing left to check.
        if (Regex.IsMatch(statement, @"^(set\s+@nr_ddl|prepare\s+nr_ddl|execute\s+nr_ddl|deallocate\s+prepare\s+nr_ddl)\b", Opts))
            return null;

        if (Regex.IsMatch(statement, $@"^create\s+table\s+(?!if\s+not\s+exists){Identifier}", Opts))
            return "CREATE TABLE without IF NOT EXISTS";
        if (Regex.IsMatch(statement, $@"^drop\s+table\s+(?!if\s+exists)", Opts))
            return "DROP TABLE without IF EXISTS";
        if (Regex.IsMatch(statement, @"^create\s+(unique\s+|fulltext\s+|spatial\s+)?index\s+(?!if\s+not\s+exists)", Opts))
            return "CREATE INDEX without IF NOT EXISTS";
        if (Regex.IsMatch(statement, @"^drop\s+index\s+(?!if\s+exists)", Opts))
            return "DROP INDEX without IF EXISTS";
        if (Regex.IsMatch(statement, @"^rename\s+table\b", Opts))
            return "bare RENAME TABLE (MariaDB has no IF EXISTS for it — use a SET @nr_ddl guard)";
        if (Regex.IsMatch(statement, @"^insert\s+into\s+`?__EFMigrationsHistory`?", Opts)
            && !Regex.IsMatch(statement, @"on\s+duplicate\s+key\s+update", Opts))
            return "__EFMigrationsHistory insert without ON DUPLICATE KEY UPDATE";

        var alter = Regex.Match(statement, $@"^alter\s+table\s+{Identifier}\s+(.*)$", Opts);
        if (!alter.Success) return null;

        var actions = SplitActions(alter.Groups[1].Value);

        // MariaDB evaluates a sibling ADD's IF NOT EXISTS against the table as it was before the
        // statement, so an ADD whose name a DROP in the same statement removes must stay unguarded —
        // guarding it would skip the re-add and lose the object outright.
        var readded = actions
            .Select(a => Regex.Match(a, $@"^\s*drop\s+(?:column|index|key|foreign\s+key|constraint)?\s*(?:if\s+exists\s+)?({Identifier})\s*$", Opts))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value.Trim('`').ToLowerInvariant())
            .ToHashSet();

        foreach (var action in actions.Select(a => a.Trim()))
        {
            if (Regex.IsMatch(action, @"^(rename\b|change(\s+column)?\s|drop\s+primary\s+key\b)", Opts))
                return "bare rename/CHANGE/DROP PRIMARY KEY (use a SET @nr_ddl guard)";
            if (Regex.IsMatch(action, $@"^add\s+(constraint\s+{Identifier}\s+)?primary\s+key\b", Opts))
                return "bare ADD PRIMARY KEY (use a SET @nr_ddl guard)";
            if (Regex.IsMatch(action, @"if\s+(not\s+)?exists", Opts)) continue;

            var added = Regex.Match(action,
                $@"^add\s+(?:column\s+|constraint\s+|(?:unique|fulltext|spatial)\s+)*(?:index\s+|key\s+)?({Identifier})", Opts);
            if (added.Success && readded.Contains(added.Groups[1].Value.Trim('`').ToLowerInvariant())) continue;

            if (Regex.IsMatch(action, @"^add\b", Opts)) return "ADD without IF NOT EXISTS";
            if (Regex.IsMatch(action, @"^drop\b", Opts)) return "DROP without IF EXISTS";
        }

        return null;
    }

    private static List<string> SplitActions(string actions)
    {
        var parts = new List<string>();
        var (depth, current) = (0, "");
        char? quote = null;

        foreach (var c in actions)
        {
            if (quote != null) { current += c; if (c == quote) quote = null; continue; }
            switch (c)
            {
                case '`' or '\'' or '"': quote = c; current += c; break;
                case '(': depth++; current += c; break;
                case ')': depth--; current += c; break;
                case ',' when depth == 0: parts.Add(current); current = ""; break;
                default: current += c; break;
            }
        }

        parts.Add(current);
        return parts.Where(p => p.Trim().Length > 0).ToList();
    }

    private static string Excerpt(string statement) =>
        statement.Length <= 90 ? statement : statement[..90] + "…";
}
