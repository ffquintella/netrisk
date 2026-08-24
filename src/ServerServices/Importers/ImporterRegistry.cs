using Contracts.Importers;
using Model.Exceptions;
using Model.Findings;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Importers;

/// <summary>
/// Discovery and resolution of scanner importers (Track 3 milestone 3.1.4).
///
/// Built-in importers and plugin importers are held in one list and resolved by the same name
/// lookup, so <c>POST /vulnerabilities/import/{importerName}/…</c> works identically for both and a
/// third party can add a scanner without touching the host.
/// </summary>
public class ImporterRegistry(ILogger logger, IPluginsService pluginsService, IDeduplicationService dedupService)
    : IImporterRegistry
{
    /// <summary>The importer name that asks the registry to sniff the report instead.</summary>
    public const string AutoDetectName = "auto";

    /// <summary>
    /// Instantiated once and reused: importers are stateless parsers, and constructing ten of them
    /// per request to answer "which importers exist" is pure waste.
    /// </summary>
    private static readonly IVulnerabilityReportImporter[] BuiltIn =
    [
        new NessusReportImporter(),
        new SarifImporter(),
        new SemgrepImporter(),
        new ZapImporter(),
        new TrivyImporter(),
        new OpenVasImporter(),
        new BurpImporter(),
        new SnykImporter(),
        new GrypeImporter(),
        new DependabotImporter()
    ];

    public async Task<List<ImporterDescriptor>> GetImportersAsync()
    {
        var descriptors = new List<ImporterDescriptor>();

        foreach (var importer in BuiltIn)
            descriptors.Add(await DescribeAsync(importer, isPlugin: false));

        foreach (var plugin in await PluginImportersAsync())
            descriptors.Add(await DescribeAsync(plugin, isPlugin: true));

        return descriptors.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IVulnerabilityReportImporter> ResolveAsync(string name)
    {
        var builtIn = BuiltIn.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (builtIn != null) return builtIn;

        var plugin = (await PluginImportersAsync())
            .FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (plugin != null) return plugin;

        // The available list travels with the error: a 404 that only says "unknown importer" makes
        // the caller guess, and the guess is usually a second failed request.
        var available = (await GetImportersAsync()).Select(d => d.Name).ToList();
        throw new DataNotFoundException("importers", name,
            new Exception($"Unknown importer '{name}'. Available importers: {string.Join(", ", available)}."));
    }

    public async Task<IVulnerabilityReportImporter> ResolveOrDetectAsync(string name, Stream report,
        string? fileName = null)
    {
        if (!string.Equals(name, AutoDetectName, StringComparison.OrdinalIgnoreCase))
            return await ResolveAsync(name);

        var detected = await DetectAsync(report, fileName);
        if (detected != null) return detected;

        var available = (await GetImportersAsync()).Select(d => d.Name).ToList();
        throw new DataNotFoundException("importers", name,
            new Exception("No importer recognised this report. Name one explicitly. " +
                          $"Available importers: {string.Join(", ", available)}."));
    }

    public async Task<IVulnerabilityReportImporter?> DetectAsync(Stream report, string? fileName = null)
    {
        var extension = string.IsNullOrWhiteSpace(fileName)
            ? null
            : Path.GetExtension(fileName).ToLowerInvariant();

        var all = BuiltIn.Concat(await PluginImportersAsync()).ToList();

        // Extension first, because it narrows the field cheaply and correctly for the common case
        // (a .nessus file is a Nessus report). Content sniffing then breaks the tie between the
        // several importers that all claim ".json".
        var byExtension = extension == null
            ? all
            : all.Where(i => i.SupportedFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                .ToList();

        var detected = Sniff(byExtension, report);
        if (detected != null) return detected;

        // The extension lied, or there was none. Try everything before giving up — a scan report
        // saved as .txt is still a scan report.
        return extension == null ? null : Sniff(all.Except(byExtension), report);
    }

    private IVulnerabilityReportImporter? Sniff(IEnumerable<IVulnerabilityReportImporter> candidates, Stream report)
    {
        foreach (var importer in candidates)
        {
            try
            {
                // The stream position is restored by ImporterHelpers.PeekText, but an importer's own
                // CanHandle could still leave it moved; resetting here means one badly-behaved
                // importer cannot break detection for the ones after it.
                if (report.CanSeek) report.Position = 0;

                if (importer.CanHandle(report)) return importer;
            }
            catch (Exception ex)
            {
                logger.Warning("Importer {Importer} threw while sniffing a report: {Message}",
                    importer.Name, ex.Message);
            }
            finally
            {
                if (report.CanSeek) report.Position = 0;
            }
        }

        return null;
    }

    private async Task<ImporterDescriptor> DescribeAsync(IVulnerabilityReportImporter importer, bool isPlugin)
    {
        string? chain = null;
        try
        {
            chain = (await dedupService.GetConfigurationAsync(importer.Name)).StrategyChain;
        }
        catch (Exception ex)
        {
            // Listing importers must work even if the dedup configuration table cannot be read;
            // the chain is informational here.
            logger.Warning("Could not read the dedup configuration for importer {Importer}: {Message}",
                importer.Name, ex.Message);
        }

        return new ImporterDescriptor
        {
            Name = importer.Name,
            DisplayName = importer.DisplayName,
            Version = importer.Version,
            ContractVersion = importer.ContractVersion,
            SupportedFileExtensions = importer.SupportedFileExtensions.ToList(),
            SupportedMimeTypes = importer.SupportedMimeTypes.ToList(),
            IsPlugin = isPlugin,
            DedupStrategyChain = chain
        };
    }

    private async Task<List<IVulnerabilityReportImporter>> PluginImportersAsync()
    {
        try
        {
            var plugins = await pluginsService.GetPluginsAsync();
            var importers = new List<IVulnerabilityReportImporter>();

            foreach (var info in plugins.Where(p => p.IsEnabled))
            {
                var importer = await TryLoadAsync(info.Name);
                if (importer == null) continue;

                // A plugin built against a newer contract than this host understands is refused
                // rather than loaded and allowed to fail mid-import with a MissingMethodException.
                if (importer.ContractVersion > ImporterContract.Version)
                {
                    logger.Warning(
                        "Importer plugin {Plugin} targets importer contract v{PluginVersion}; this server implements v{HostVersion}. Skipping it.",
                        info.Name, importer.ContractVersion, ImporterContract.Version);
                    continue;
                }

                importers.Add(importer);
            }

            return importers;
        }
        catch (Exception ex)
        {
            // Plugin discovery touches the file system. A broken plugin directory must not take the
            // built-in importers down with it.
            logger.Warning("Could not enumerate importer plugins: {Message}", ex.Message);
            return [];
        }
    }

    private async Task<INetriskVulnerabilityImporterPlugin?> TryLoadAsync(string name)
    {
        try
        {
            return await pluginsService.GetPluginAsync<INetriskVulnerabilityImporterPlugin>(name);
        }
        catch (Exception)
        {
            // Expected for every plugin that is not an importer.
            return null;
        }
    }
}
