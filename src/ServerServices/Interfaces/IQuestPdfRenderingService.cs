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

    /// <summary>
    /// Renders the first page of a template to a PNG image using a small set of sample
    /// data, so a designer UI can show a live, branded preview without a real data set.
    /// </summary>
    /// <param name="layoutJson">The JSON definition of the report layout/sections.</param>
    /// <param name="brandingJson">The JSON definition of colors, logos, and fonts.</param>
    /// <param name="reportTitle">The title to show in the preview header.</param>
    /// <returns>A byte array of the rendered PNG image (first page).</returns>
    Task<byte[]> RenderPreviewImageAsync(string layoutJson, string brandingJson, string reportTitle);
}
