using DAL.Entities;
using ServerServices.Interfaces;
using ServerServices.Interfaces.Importers;

namespace ServerServices.Services.Importers;

public class ImporterFactory(IHostsService hostsService, IVulnerabilitiesService vulnerabilitiesService) : IVulnerabilityImporterFactory
{
    private IHostsService HostsService { get; } = hostsService;
    private IVulnerabilitiesService VulnerabilitiesService { get; } = vulnerabilitiesService;

    public IVulnerabilityImporter GetImporter(string impoterType, User? user)
    {
        switch (impoterType.ToLower())
        {
            case "tenable nessus":
                return new NessusImporter(HostsService, VulnerabilitiesService, user);
            //case "secure scorecard":
            //    return new SecureScoreCardImporter();
            default:
                throw new Exception("Importer not found");
            
        }
    }
}