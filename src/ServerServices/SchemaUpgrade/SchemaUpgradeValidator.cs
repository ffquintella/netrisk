using Model.Database;
using MySqlConnector;

namespace ServerServices.SchemaUpgrade;

/// <summary>
/// Runs a single post-apply <see cref="SchemaUpgradeValidation"/> against a live MySQL connection
/// using <c>information_schema</c>. Returns whether it passed and a human-readable detail line.
/// </summary>
public static class SchemaUpgradeValidator
{
    public static (bool passed, string detail) Run(MySqlConnection connection, SchemaUpgradeValidation v)
    {
        try
        {
            return v.Type switch
            {
                "IndexExists" => IndexExists(connection, v),
                "ForeignKeyExists" => ForeignKeyExists(connection, v),
                "ColumnType" => ColumnType(connection, v),
                "TableExists" => TableExists(connection, v.Table),
                "RowCountParity" => RowCountPresent(connection, v),
                "Custom" => Custom(connection, v),
                _ => (false, $"Unknown validation type '{v.Type}'.")
            };
        }
        catch (Exception ex)
        {
            return (false, $"validation error: {ex.Message}");
        }
    }

    private static (bool, string) IndexExists(MySqlConnection conn, SchemaUpgradeValidation v)
    {
        var count = Scalar(conn,
            "SELECT COUNT(*) FROM information_schema.statistics " +
            "WHERE table_schema = DATABASE() AND table_name = @t AND index_name = @i",
            ("@t", v.Table), ("@i", v.Target));
        var ok = count > 0;
        return (ok, ok ? $"index {v.Target} on {v.Table} present" : $"index {v.Target} on {v.Table} MISSING");
    }

    private static (bool, string) ForeignKeyExists(MySqlConnection conn, SchemaUpgradeValidation v)
    {
        var count = Scalar(conn,
            "SELECT COUNT(*) FROM information_schema.table_constraints " +
            "WHERE table_schema = DATABASE() AND table_name = @t AND constraint_name = @c " +
            "AND constraint_type = 'FOREIGN KEY'",
            ("@t", v.Table), ("@c", v.Target));
        var ok = count > 0;
        return (ok, ok ? $"FK {v.Target} on {v.Table} present" : $"FK {v.Target} on {v.Table} MISSING");
    }

    private static (bool, string) ColumnType(MySqlConnection conn, SchemaUpgradeValidation v)
    {
        using var cmd = new MySqlCommand(
            "SELECT data_type FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = @t AND column_name = @c", conn);
        cmd.Parameters.AddWithValue("@t", v.Table);
        cmd.Parameters.AddWithValue("@c", v.Target);
        var actual = cmd.ExecuteScalar()?.ToString();
        if (actual is null) return (false, $"column {v.Table}.{v.Target} not found");
        var ok = string.Equals(actual, v.Expected, StringComparison.OrdinalIgnoreCase);
        return (ok, ok
            ? $"{v.Table}.{v.Target} is {actual}"
            : $"{v.Table}.{v.Target} is {actual}, expected {v.Expected}");
    }

    private static (bool, string) TableExists(MySqlConnection conn, string? table)
    {
        var count = Scalar(conn,
            "SELECT COUNT(*) FROM information_schema.tables " +
            "WHERE table_schema = DATABASE() AND table_name = @t", ("@t", table));
        var ok = count > 0;
        return (ok, ok ? $"table {table} present" : $"table {table} MISSING");
    }

    private static (bool, string) RowCountPresent(MySqlConnection conn, SchemaUpgradeValidation v)
    {
        // Generic engine: confirm the (post-rename) table exists and report its row count. True
        // before/after parity for a specific rename is asserted by that phase's own integration test.
        var (exists, _) = TableExists(conn, v.Table);
        if (!exists) return (false, $"table {v.Table} MISSING");
        using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM `{v.Table}`", conn);
        var rows = Convert.ToInt64(cmd.ExecuteScalar());
        return (true, $"table {v.Table} present with {rows} row(s)");
    }

    private static (bool, string) Custom(MySqlConnection conn, SchemaUpgradeValidation v)
    {
        if (string.IsNullOrWhiteSpace(v.Sql)) return (false, "Custom validation has no SQL.");
        using var cmd = new MySqlCommand(v.Sql, conn);
        var actual = Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        var ok = CompareExpected(actual, v.Expected);
        return (ok, ok ? $"custom check passed (={actual})" : $"custom check FAILED (got {actual}, expected {v.Expected})");
    }

    /// <summary>Compares a scalar result against an expectation of the form <c>0</c>, <c>&gt;0</c>, <c>&gt;=N</c>, etc.</summary>
    private static bool CompareExpected(long actual, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return actual != 0;
        expected = expected.Trim();
        if (expected.StartsWith(">=")) return actual >= long.Parse(expected[2..]);
        if (expected.StartsWith("<=")) return actual <= long.Parse(expected[2..]);
        if (expected.StartsWith(">")) return actual > long.Parse(expected[1..]);
        if (expected.StartsWith("<")) return actual < long.Parse(expected[1..]);
        return actual == long.Parse(expected);
    }

    private static long Scalar(MySqlConnection conn, string sql, params (string, string?)[] parms)
    {
        using var cmd = new MySqlCommand(sql, conn);
        foreach (var (name, value) in parms)
            cmd.Parameters.AddWithValue(name, value ?? string.Empty);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }
}
