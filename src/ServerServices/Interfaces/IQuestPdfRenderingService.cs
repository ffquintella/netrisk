using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServerServices.Interfaces;

public interface IQuestPdfRenderingService
{
    /// <summary>
    /// Renders a dynamic PDF report using a template layout and branding specifications.
    /// </summary>
    /// <typeparam name="T">The type of the data collection items.</typeparam>
    /// <param name="layoutJson">The JSON definition of the report layout/sections.</param>
    /// <param name="brandingJson">The JSON definition of colors, logos, and fonts.</param>
    /// <param name="data">The collection of data to present in the report.</param>
    /// <param name="reportTitle">The main title of the generated report.</param>
    /// <returns>A byte array of the generated PDF document.</returns>
    Task<byte[]> RenderFromTemplateAsync<T>(string layoutJson, string brandingJson, IEnumerable<T> data, string reportTitle);
}
