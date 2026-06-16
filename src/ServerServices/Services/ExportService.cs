using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ServerServices.Interfaces;
using ClosedXML.Excel;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class ExportService(ILogger logger, IDalService dalService, ILocalizationService localization, IQuestPdfRenderingService questPdfRenderingService)
    : LocalizableService(logger, dalService, localization), IExportService
{
    public async Task<byte[]> ExportAsync<T>(IEnumerable<T> data, ExportFormat format, string reportTitle)
    {
        Logger.Information("Starting export for data type {Type} in format {Format} with title '{Title}'", typeof(T).Name, format, reportTitle);

        try
        {
            return format switch
            {
                ExportFormat.Csv => await Task.Run(() => ExportToCsv(data)),
                ExportFormat.Xlsx => await Task.Run(() => ExportToXlsx(data, reportTitle)),
                ExportFormat.Pdf => await questPdfRenderingService.RenderFromTemplateAsync(null!, null!, data, reportTitle),
                _ => throw new ArgumentException($"Unsupported export format: {format}", nameof(format))
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error exporting data of type {Type} in format {Format}", typeof(T).Name, format);
            throw;
        }
    }

    private byte[] ExportToCsv<T>(IEnumerable<T> data)
    {
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, new UTF8Encoding(true))) // true forces UTF-8 BOM
        {
            var properties = GetActiveProperties<T>();

            // Write Headers
            var headerLine = new List<string>();
            foreach (var prop in properties)
            {
                headerLine.Add(EscapeCsvCell(prop.Name));
            }
            writer.WriteLine(string.Join(",", headerLine));

            // Write Rows
            foreach (var item in data)
            {
                var rowLine = new List<string>();
                foreach (var prop in properties)
                {
                    var val = prop.GetValue(item);
                    rowLine.Add(EscapeCsvCell(val?.ToString()));
                }
                writer.WriteLine(string.Join(",", rowLine));
            }
        }
        return ms.ToArray();
    }

    private byte[] ExportToXlsx<T>(IEnumerable<T> data, string reportTitle)
    {
        using var workbook = new XLWorkbook();
        
        // Excel worksheet name limit is 30 characters
        var sheetName = reportTitle.Length > 30 ? reportTitle.Substring(0, 30) : reportTitle;
        // Escape special chars in sheet name that Excel prohibits
        sheetName = EscapeSheetName(sheetName);
        
        var worksheet = workbook.Worksheets.Add(sheetName);

        var properties = GetActiveProperties<T>();

        // Write Headers
        int colIndex = 1;
        foreach (var prop in properties)
        {
            worksheet.Cell(1, colIndex++).Value = prop.Name;
        }

        // Write Rows
        int rowIndex = 2;
        foreach (var item in data)
        {
            colIndex = 1;
            foreach (var prop in properties)
            {
                var val = prop.GetValue(item);
                var cell = worksheet.Cell(rowIndex, colIndex++);

                if (val == null)
                {
                    cell.Value = string.Empty;
                }
                else if (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(int?) ||
                         prop.PropertyType == typeof(long) || prop.PropertyType == typeof(long?) ||
                         prop.PropertyType == typeof(short) || prop.PropertyType == typeof(short?))
                {
                    cell.Value = Convert.ToInt64(val);
                }
                else if (prop.PropertyType == typeof(double) || prop.PropertyType == typeof(double?) ||
                         prop.PropertyType == typeof(float) || prop.PropertyType == typeof(float?) ||
                         prop.PropertyType == typeof(decimal) || prop.PropertyType == typeof(decimal?))
                {
                    cell.Value = Convert.ToDouble(val);
                }
                else if (prop.PropertyType == typeof(bool) || prop.PropertyType == typeof(bool?))
                {
                    cell.Value = (bool)val;
                }
                else if (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?))
                {
                    cell.Value = (DateTime)val;
                }
                else
                {
                    cell.Value = val.ToString();
                }
            }
            rowIndex++;
        }

        // Apply visual styling to Header row
        if (properties.Count > 0)
        {
            var headerRange = worksheet.Range(1, 1, 1, properties.Count);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2A3F54");
            headerRange.Style.Font.FontColor = XLColor.White;
        }

        worksheet.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private List<PropertyInfo> GetActiveProperties<T>()
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var activeProps = new List<PropertyInfo>();

        foreach (var prop in properties)
        {
            // Skip complex objects/collections (virtual or non-primitive classes), except string
            if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string))
                continue;

            // Skip compiler-generated backing fields or collection types
            if (prop.PropertyType.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType))
                continue;

            activeProps.Add(prop);
        }

        return activeProps;
    }

    private string EscapeCsvCell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        // Prevent CSV Formula Injection (CWE-1236)
        if (value.StartsWith("=") || value.StartsWith("+") || value.StartsWith("-") || value.StartsWith("@"))
        {
            value = "'" + value;
        }

        // Escape double quotes and wrap in quotes if it has commas or newlines
        if (value.Contains("\"") || value.Contains(",") || value.Contains("\n") || value.Contains("\r"))
        {
            value = "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private string EscapeSheetName(string name)
    {
        // Excel sheet names cannot contain characters like: \, /, ?, *, [, ]
        var charsToRemove = new[] { '\\', '/', '?', '*', '[', ']' };
        foreach (var c in charsToRemove)
        {
            name = name.Replace(c, ' ');
        }
        return name;
    }
}
