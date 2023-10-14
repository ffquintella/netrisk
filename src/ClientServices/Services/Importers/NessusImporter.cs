using System.Xml.Linq;
using ClientServices.Events;
using ClientServices.Interfaces;
using ClientServices.Interfaces.Importers;
using DAL.Entities;
using Model;
using Model.DTO;
using nessus_tools;
using Tools.Security;

namespace ClientServices.Services.Importers;

public class NessusImporter: BaseImporter, IVulnerabilityImporter
{
    private IHostsService HostsService { get; } = GetService<IHostsService>();
    
    
    public async Task<int> Import(string filePath)
    {
        int importedVulnerabilities = 0;

        if (!File.Exists(filePath)) throw new FileNotFoundException("File not found");
        

        int interactions = 0;

        await Task.Run(() =>
        {
            NessusClientData_v2 nessusClientData = NessusClientData_v2.Parse(filePath);

            var hostNumber = nessusClientData.Report.ReportHosts.Count;

            int vulnNumber = 0;

            foreach (ReportHost host in nessusClientData.Report.ReportHosts)
            {
                vulnNumber += host.ReportItems.Count;
            }

            var tic = vulnNumber / 100;

            foreach (ReportHost host in nessusClientData.Report.ReportHosts)
            {
                
                // First let´s check if the host already exists
                
                var hostExists = HostsService.HostExists(host.IpAddress);

                Host nrHost;
                
                if (hostExists)
                {
                    nrHost = HostsService.GetByIp(host.IpAddress)!;
                    nrHost.LastVerificationDate = DateTime.Now;
                    nrHost.Status = (short) IntStatus.Active;
                    HostsService.Update(nrHost);
                }
                else
                {
                    nrHost = new Host()
                    {
                        Ip = host.IpAddress,
                        HostName = host.Name,
                        Fqdn = host.FQDN,
                        MacAddress = host.MacAddress,
                        LastVerificationDate = DateTime.Now,
                        RegistrationDate = DateTime.Now,
                        Source = "Nessus",
                        Status = (short) IntStatus.Active,
                        TeamId = 2,
                        Comment = "Created by Nessus Importer",
                    };
                    var newHost = HostsService.Create(nrHost);
                    nrHost = newHost!;
                }
                
                
                
                foreach (ReportItem item in host.ReportItems)
                {

                    //Dealing with the service
                    var serviceExists = HostsService.HostHasService(nrHost.Id, item.ServiceName, item.Port, item.Protocol);
                    DAL.Entities.HostsService nrService;
                    if (!serviceExists)
                    {
                        var service = new HostsServiceDto()
                        {
                            
                            Name = item.ServiceName,
                            Port = item.Port,
                            Protocol = item.Protocol,
          
                        };
                        nrService = HostsService.CreateAndAddService(nrHost.Id, service);
                    }
                    else
                    {
                        nrService = HostsService.FindService(nrHost.Id, item.ServiceName, item.Port, item.Protocol)!;
                    }

                    var vulHashString = item.Plugin_Name + nrHost.Id + item.Severity + item.Risk_Factor + nrService!.Id;
                    var hash = HashTool.CreateSha1(vulHashString);
                
                    var vulnerability = new Vulnerability
                    {
                        Title = item.Plugin_Name,
                        Description = item.Description,
                        Severity = item.Severity,
                        Solution = item.Solution,
                        Details = item.Plugin_Output,
                        DetectionCount = 1,
                        LastDetection = DateTime.Now,
                        FirstDetection = DateTime.Now,
                        Status = (ushort) IntStatus.New,
                        HostId = nrHost.Id,
                        FixTeamId = 1,
                        Technology = "Not Specified",
                        ImportSorce = "nessus",
                        HostServiceId = nrService!.Id,
                        ImportHash = hash

                    };
                    

                }
            }
        });


        return importedVulnerabilities;
    }

    private void CompleteStep()
    {
        var pc = new ProgressBarrEventArgs {Progess = 1};
        NotifyStepCompleted(pc);
    }
    
    
    public event EventHandler<ProgressBarrEventArgs>? StepCompleted;
    
    private void NotifyStepCompleted(ProgressBarrEventArgs pc)
    {
        EventHandler<ProgressBarrEventArgs>? handler = StepCompleted;
        if (handler != null) handler(this, pc);
    }

}