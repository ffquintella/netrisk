using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServerServices.Interfaces;

public enum ExportFormat
{
    Csv,
    Xlsx,
    Pdf
}

public interface IExportService
{
    /// <summary>
    /// Exports the provided data collection into the specified format (CSV, XLSX, PDF).
    /// </summary>
    /// <typeparam name="T">The type of the data elements.</typeparam>
    /// <param name="data">The collection of data to export.</param>
    /// <param name="format">The target export format.</param>
    /// <param name="reportTitle">The title of the generated report/file.</param>
    /// <returns>A byte array representing the generated file content.</returns>
    Task<byte[]> ExportAsync<T>(IEnumerable<T> data, ExportFormat format, string reportTitle);
}
