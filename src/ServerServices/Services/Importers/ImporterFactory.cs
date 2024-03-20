using ServerServices.Interfaces;
using ServerServices.Interfaces.Importers;

namespace ServerServices.Services.Importers;

public class ImporterFactory(IHostsService hostsService) : IVulnerabilityImporterFactory
{
    private IHostsService HostsService { get; } = hostsService;

    public IVulnerabilityImporter GetImporter(string impoterType)
    {
        switch (impoterType.ToLower())
        {
            case "tenable nessus":
                return new NessusImporter(HostsService);
            //case "secure scorecard":
            //    return new SecureScoreCardImporter();
            default:
                throw new Exception("Importer not found");
            
        }
    }
}