using DAL.Entities;
using ServerServices.Interfaces;
using ServerServices.Interfaces.Importers;

namespace ServerServices.Services.Importers;

public class ImporterFactory(IHostsService hostsService, IVulnerabilitiesService vulnerabilitiesService, JobManager jobManager, IJobsService jobsService) : IVulnerabilityImporterFactory
{
    private IHostsService HostsService { get; } = hostsService;
    private IVulnerabilitiesService VulnerabilitiesService { get; } = vulnerabilitiesService;
    private IJobsService JobsService { get; } = jobsService;
    private JobManager JobManager { get; } = jobManager;

    public IVulnerabilityImporter GetImporter(string impoterType, User? user)
    {
        switch (impoterType.ToLower())
        {
            case "tenable nessus":
                return new NessusImporter(HostsService, VulnerabilitiesService, JobManager, JobsService, user);
            //case "secure scorecard":
            //    return new SecureScoreCardImporter();
            default:
                throw new Exception("Importer not found");
            
        }
    }
}