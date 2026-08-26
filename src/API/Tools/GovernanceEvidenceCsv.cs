using System.Globalization;
using System.Text;
using DAL.Entities;

namespace API.Controllers;

/// <summary>
/// Renders the field-level governance trail as CSV (Track 8 milestone 8.4.2).
///
/// Its own type rather than a method on the controller so it can be unit-tested without an HTTP
/// context: the escaping is the part that goes wrong, and a justification field containing a comma
/// and a newline is the normal case here, not an edge one.
/// </summary>
public static class GovernanceEvidenceCsv
{
    public static string Render(IEnumerable<AuditLog> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("occurred_at_utc,entity_type,entity_id,field,action,actor,user_id,old_value,new_value,correlation_id");

        foreach (var row in rows)
            builder
                .Append(row.OccurredAt.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(row.EntityType)).Append(',')
                .Append(row.EntityId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(row.Field)).Append(',')
                .Append(Escape(row.Action.ToString())).Append(',')
                .Append(Escape(row.Actor)).Append(',')
                .Append(row.UserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(Escape(row.OldValue)).Append(',')
                .Append(Escape(row.NewValue)).Append(',')
                .Append(Escape(row.CorrelationId))
                .AppendLine();

        return builder.ToString();
    }

    /// <summary>
    /// RFC 4180 quoting, plus a formula guard.
    ///
    /// A justification beginning with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is executed as a
    /// formula by Excel and by Google Sheets when the file is opened. The evidence pack is a file
    /// that gets emailed to an auditor and opened in a spreadsheet, which is precisely the CSV
    /// injection scenario, so a leading tab is prepended to neutralise it while leaving the text
    /// readable.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        if (value[0] is '=' or '+' or '-' or '@') value = "\t" + value;

        if (value.IndexOfAny([',', '"', '\n', '\r', '\t']) < 0) return value;

        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
