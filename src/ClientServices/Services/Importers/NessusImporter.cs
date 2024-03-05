using ClientServices.Events;
using ClientServices.Interfaces;
using ClientServices.Interfaces.Importers;
using DAL.Entities;
using Model;
using Model.DTO;
using nessus_tools;
using Tools.Math;
using Tools.Security;
using System.Linq;

namespace ClientServices.Services.Importers;

public class NessusImporter: BaseImporter, IVulnerabilityImporter
{
    private IHostsService HostsService { get; } = GetService<IHostsService>();
    private IAuthenticationService AuthenticationService { get; } = GetService<IAuthenticationService>();
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    
    
    public async Task<int> Import(string filePath, bool ignoreNegligible = true)
    {
        int importedVulnerabilities = 0;

        if (!File.Exists(filePath)) throw new FileNotFoundException("File not found");
        
        NessusClientData_v2 nessusClientData = NessusClientData_v2.Parse(filePath);

        //var hostNumber = nessusClientData.Report.ReportHosts.Count;

        int vulnNumber = 0;

        /*foreach (ReportHost host in nessusClientData.Report.ReportHosts)
        {
            vulnNumber += host.ReportItems.Count;
        }*/
        
        var ReportHosts = new List<ReportHost>(nessusClientData.Report.ReportHosts.Cast<ReportHost>());
        
        Parallel.ForEach(ReportHosts, host  =>
        {
            vulnNumber += host.ReportItems.Count;
        });
        
        int interactions = 0;
        
        var tic = DivisionHelper.RoundedDivision(vulnNumber, 100);

        //foreach (ReportHost host in nessusClientData.Report.ReportHosts)

        var processingTaks = ReportHosts.Select(async host =>
        {

            string hostProperties = "";
            
            Parallel.ForEach(host.HostProperties.Tags, tag  =>
            {
                hostProperties += tag.Name + ":" + tag.Value + "\n";
            });
            
            
            // First let´s check if the host already exists
            var hostExists = await HostsService.HostExistsAsync(host.IpAddress);
            
            
            Host nrHost;
            if (hostExists)
            {
                nrHost = await HostsService.GetByIpAsync(host.IpAddress)!;
                nrHost.LastVerificationDate = DateTime.Now;
                nrHost.Status = (short)IntStatus.Active; 
                HostsService.UpdateAsync(nrHost);
            }
            else
            {
                nrHost = new Host()
                {
                    Ip = host.IpAddress,
                    HostName = host.Name,
                    Fqdn = host.FQDN,
                    MacAddress = host.MacAddress,
                    Os = host.OS,
                    LastVerificationDate = DateTime.Now,
                    RegistrationDate = DateTime.Now,
                    Source = "Nessus",
                    Status = (short)IntStatus.Active,
                    TeamId = 2,
                    Comment = "Created by Nessus Importer",
                    Properties = hostProperties
                };
                var newHost = await HostsService.Create(nrHost);
                nrHost = newHost!;
            }



            foreach (ReportItem item in host.ReportItems)
            {

                //Dealing with the service
                var serviceExists =
                    await HostsService.HostHasServiceAsync(nrHost.Id, item.ServiceName, item.Port, item.Protocol);
                HostsService? nrService;
                if (!serviceExists)
                {
                    var service = new HostsServiceDto()
                    {

                        Name = item.ServiceName,
                        Port = item.Port,
                        Protocol = item.Protocol,

                    };
                    nrService = await HostsService.CreateAndAddServiceAsync(nrHost.Id, service);
                }
                else
                {
                    nrService = await HostsService.FindServiceAsync(nrHost.Id, item.ServiceName, item.Port,
                        item.Protocol)!;
                }

                if (ignoreNegligible && item.Severity == "0")
                {
                    interactions++;

                    var rest = Convert.ToInt32(interactions % tic);
                    if (rest == 0) CompleteStep();

                    continue;
                }

                var vulHashString = item.Plugin_Name + nrHost.Id + item.Severity + item.Risk_Factor + nrService!.Id;
                var hash = HashTool.CreateSha1(vulHashString);

                var vulFindResult = await VulnerabilitiesService.FindAsync(hash);

                var action = new NrAction()
                {
                    DateTime = DateTime.Now,
                    Message = "Created by Nessus Importer",
                    UserId = AuthenticationService.AuthenticatedUserInfo!.UserId,
                    ObjectType = nameof(Vulnerability)

                };
                var userid = AuthenticationService.AuthenticatedUserInfo!.UserId!.Value;

                if (vulFindResult.Item1)
                {
                    //Vulnerability already exists
                    var vulnerability = vulFindResult.Item2!;
                    vulnerability.DetectionCount++;
                    vulnerability.LastDetection = DateTime.Now;
                    VulnerabilitiesService.Update(vulnerability);

                    action.Message = "Notified by Nessus Importer";

                    await VulnerabilitiesService.AddActionAsync(vulnerability.Id, userid, action);

                }
                else
                {
                    var vulnerability = new Vulnerability
                    {
                        Title = item.Plugin_Name,
                        Description = item.Description,
                        Severity = ConvertCriticalityToInt(item.Criticality).ToString(),
                        Solution = item.Solution,
                        Details = item.Plugin_Output,
                        DetectionCount = 1,
                        LastDetection = DateTime.Now,
                        FirstDetection = DateTime.Now,
                        Status = (ushort)IntStatus.New,
                        HostId = nrHost.Id,
                        FixTeamId = 1,
                        Technology = "Not Specified",
                        ImportSource = "nessus",
                        HostServiceId = nrService!.Id,
                        ImportHash = hash,
                        AnalystId = AuthenticationService.AuthenticatedUserInfo!.UserId,
                        Score = Int32.Parse(item.Severity),

                    };
                    var vul = await VulnerabilitiesService.CreateAsync(vulnerability);
                    await VulnerabilitiesService.AddActionAsync(vul.Id, userid, action);
                }

                interactions++;
                importedVulnerabilities++;
                var rest2 = Convert.ToInt32(interactions % tic);
                if (rest2 == 0) CompleteStep();

            }
        });
        await Task.WhenAll(processingTaks);
        
        return importedVulnerabilities;
    }

    private void CompleteStep()
    {
        var pc = new ProgressBarrEventArgs {Progess = 1};
        NotifyStepCompleted(pc);
    }

    public int ConvertCriticalityToInt(Criticality criticality)
    {
        switch(criticality)
        {
            case Criticality.None:
                return 0;
            case Criticality.Low:
                return 1;
            case Criticality.Medium:
                return 2;
            case Criticality.High:
                return 3;
            case Criticality.Critical:
                return 4;
            default:
                return 0;
        }
    }
    
    public event EventHandler<ProgressBarrEventArgs>? StepCompleted;
    
    private void NotifyStepCompleted(ProgressBarrEventArgs pc)
    {
        EventHandler<ProgressBarrEventArgs>? handler = StepCompleted;
        if (handler != null) handler(this, pc);
    }

}