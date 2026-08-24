using Contracts.Importers;
using Model.Findings;

namespace ServerServices.Interfaces;

/// <summary>
/// Discovery and resolution of scanner importers (Track 3 milestone 3.1.4). Built-ins and plugins
/// are indistinguishable to callers.
/// </summary>
public interface IImporterRegistry
{
    /// <summary>Everything available to import with, for the API listing and the GUI picker.</summary>
    Task<List<ImporterDescriptor>> GetImportersAsync();

    /// <summary>
    /// The importer with this name. Throws <see cref="Model.Exceptions.DataNotFoundException"/> —
    /// carrying the available names — for an unknown one.
    /// </summary>
    Task<IVulnerabilityReportImporter> ResolveAsync(string name);

    /// <summary>
    /// As <see cref="ResolveAsync"/>, except that the reserved name <c>auto</c> sniffs the report's
    /// content instead.
    /// </summary>
    Task<IVulnerabilityReportImporter> ResolveOrDetectAsync(string name, Stream report, string? fileName = null);

    /// <summary>
    /// Which importer recognises this report, or null if none does. Uses the file extension to
    /// narrow the field and content sniffing to decide.
    /// </summary>
    Task<IVulnerabilityReportImporter?> DetectAsync(Stream report, string? fileName = null);
}
