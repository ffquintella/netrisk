using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ClosedXML.Excel;

namespace GUIClient.Tools;

/// <summary>
/// Client-side exporter for the data shown on the Reports views ("what you see is what
/// you export"). Produces CSV (UTF-8 BOM, formula-injection-escaped) and typed XLSX
/// (ClosedXML) directly from in-memory row collections — no server round-trip, because
/// these views show computed projections the server export endpoint doesn't model.
/// </summary>
public static class GridDataExporter
{
    private static List<PropertyInfo> GetColumns<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && IsExportable(p.PropertyType))
            .ToList();

    private static bool IsExportable(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u.IsPrimitive || u.IsEnum || u == typeof(string) || u == typeof(decimal)
               || u == typeof(DateTime) || u == typeof(DateTimeOffset) || u == typeof(Guid);
    }

    #region Strongly-typed row export

    public static byte[] ToCsv<T>(IEnumerable<T> rows)
    {
        var columns = GetColumns<T>();
        var sb = new StringBuilder();

        sb.AppendLine(string.Join(",", columns.Select(c => CsvCell(c.Name))));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", columns.Select(c => CsvCell(FormatValue(c.GetValue(row))))));

        // UTF-8 BOM so Excel detects the encoding.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public static byte[] ToXlsx<T>(IEnumerable<T> rows, string sheetName = "Export")
    {
        var columns = GetColumns<T>();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Sanitize(sheetName));

        for (var c = 0; c < columns.Count; c++)
            ws.Cell(1, c + 1).Value = columns[c].Name;
        ws.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < columns.Count; c++)
                SetTypedCell(ws.Cell(r, c + 1), columns[c].GetValue(row));
            r++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    #endregion

    #region Chart-series export (flattens LiveCharts ISeries into Series/Index/Value rows)

    private sealed class SeriesRow
    {
        public string Series { get; init; } = "";
        public int Index { get; init; }
        public string Value { get; init; } = "";
    }

    private static List<SeriesRow> FlattenSeries(IEnumerable series)
    {
        var rows = new List<SeriesRow>();
        foreach (var s in series)
        {
            if (s == null) continue;
            var type = s.GetType();
            var name = type.GetProperty("Name")?.GetValue(s) as string ?? "Series";
            var values = type.GetProperty("Values")?.GetValue(s) as IEnumerable;
            if (values == null) continue;

            var i = 0;
            foreach (var v in values)
                rows.Add(new SeriesRow { Series = name, Index = i++, Value = FormatValue(v) });
        }
        return rows;
    }

    public static byte[] SeriesToCsv(IEnumerable series) => ToCsv(FlattenSeries(series));
    public static byte[] SeriesToXlsx(IEnumerable series) => ToXlsx(FlattenSeries(series));

    #endregion

    #region Helpers

    private static void SetTypedCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Value = string.Empty;
                break;
            case DateTime dt:
                cell.Value = dt;
                break;
            case DateTimeOffset dto:
                cell.Value = dto.DateTime;
                break;
            case bool b:
                cell.Value = b;
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                cell.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    // CSV escaping + spreadsheet formula-injection guard (prefix =, +, -, @).
    private static string CsvCell(string value)
    {
        var escaped = value;
        if (escaped.Length > 0 && (escaped[0] is '=' or '+' or '-' or '@'))
            escaped = "'" + escaped;

        if (escaped.Contains('"') || escaped.Contains(',') || escaped.Contains('\n') || escaped.Contains('\r'))
            escaped = "\"" + escaped.Replace("\"", "\"\"") + "\"";

        return escaped;
    }

    private static string Sanitize(string sheetName)
    {
        var invalid = new[] { '\\', '/', '*', '[', ']', ':', '?' };
        var clean = new string(sheetName.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "Export" : clean[..Math.Min(31, clean.Length)];
    }

    #endregion
}
