using System.IO;
using System.Threading.Tasks;
using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IImportsService
{
    /// <summary>
    /// Imports a standard GRC assessment template from JSON content.
    /// </summary>
    /// <param name="jsonContent">The raw JSON schema string.</param>
    /// <returns>The newly created Assessment entity.</returns>
    Task<Assessment> ImportAssessmentFromJsonAsync(string jsonContent);

    /// <summary>
    /// Imports a standard GRC assessment template from an Excel spreadsheet stream.
    /// </summary>
    /// <param name="excelStream">The stream of the Excel spreadsheet file.</param>
    /// <param name="assessmentName">The name of the new assessment to create.</param>
    /// <returns>The newly created Assessment entity.</returns>
    Task<Assessment> ImportAssessmentFromExcelAsync(Stream excelStream, string assessmentName);
}
