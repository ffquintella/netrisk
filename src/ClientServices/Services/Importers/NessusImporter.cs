using System.Xml.Linq;
using ClientServices.Events;
using ClientServices.Interfaces.Importers;
using DAL.Entities;
using nessus_tools;

namespace ClientServices.Services.Importers;

public class NessusImporter: IVulnerabilityImporter
{
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
                
                var nrHost = new Host()
                {

                };
                
                foreach (ReportItem item in host.ReportItems)
                {


                    var vulnerability = new Vulnerability
                    {
                        Title = item.Plugin_Name,
                        Description = item.Description,
                        //Severity = item.Risk_Factor,
                        Solution = item.Solution,
                        //PluginId = item.PluginID,
                        //PluginFamily = item.PluginFamily,
                        Details = item.Plugin_Output,
                        Port = item.Port,
                        Protocol = item.Protocol,
                        Severity = item.Severity,
                        Service = item.ServiceName,
                        State = item.State,
                        Host = host.HostProperties.HostName,
                        HostIp = host.HostProperties.HostIP,
                        HostFqdn = host.HostProperties.Fqdn,
                        HostMac = host.HostProperties.MacAddress,
                        HostNetbios = host.HostProperties.NetbiosName,
                        HostOs = host.HostProperties.OsName,
                        HostOsCpe = host.HostProperties.OsCpe,
                        HostOsFingerprint = host.HostProperties.OsFingerprint,
                        HostOsLang = host.HostProperties.OsLang,
                        HostStartTime = host.HostProperties.StartTime,
                        HostEndTime = host.HostProperties.EndTime,
                        HostTags = host.HostProperties.Tags,
                        HostVlan = host.HostProperties.Vlan,
                        HostHostId = host.HostProperties.HostId,
                        HostHostIndex = host.HostProperties.HostIndex,
                        HostHostIp = host.HostProperties.HostIp,
                        HostHostFqdn = host.HostProperties.HostFqdn,
                        HostHostMac = host.HostProperties.HostMac,
                        HostHostNetbios = host.HostProperties.HostNetbios,
                        HostHostOs = host.HostProperties.HostOs,
                        HostHostOsCpe = host.HostProperties.HostOsCpe,
                        HostHostOsFingerprint = host.HostProperties.HostOsFingerprint,
                        HostHostOsLang = host.HostProperties.HostOsLang,
                        HostHostStartTime = host.HostProperties.HostStartTime,
                        HostHostEndTime = host.HostProperties.HostEndTime,
                        HostHostTags = host.HostProperties.HostTags,
                        HostHostVlan = host.HostProperties.HostVlan,
                        HostHostHostId = host.HostProperties.HostHostId,
                        HostHostHostIndex = host.HostProperties.HostHostIndex,
                        HostHostHostIp = host.HostProperties.HostHostIp,
                        HostHostHostFqdn = host.HostProperties.HostHostFqdn

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