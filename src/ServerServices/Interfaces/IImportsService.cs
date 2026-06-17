using System.IO;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Assessments;

namespace ServerServices.Interfaces;

public interface IImportsService
{
    /// <summary>
    /// Dry-run: validates JSON template content and returns a summary (pages, questions,
    /// warnings, row-level errors) without writing anything to the database.
    /// </summary>
    Task<AssessmentImportPreview> PreviewAssessmentFromJsonAsync(string jsonContent);

    /// <summary>
    /// Dry-run: validates an Excel template stream and returns a summary without writing
    /// anything to the database.
    /// </summary>
    Task<AssessmentImportPreview> PreviewAssessmentFromExcelAsync(Stream excelStream, string assessmentName);

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
