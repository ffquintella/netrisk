﻿using DAL.Entities;
using Model;
using Model.DTO;
using nessus_tools;
using Tools.Math;
using Tools.Security;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using ServerServices.Interfaces;
using ServerServices.Interfaces.Importers;
using System.Globalization;
using Serilog;
using Model.Exceptions;
using ServerServices.Events;
using Tools.Extensions;
using User = DAL.Entities.User;

namespace ServerServices.Services.Importers;

public class NessusImporter(IHostsService hostsService, 
    IVulnerabilitiesService vulnerabilitiesService, 
    JobManager jobManager,
    IJobsService jobsService, 
    DAL.Entities.User? user) : 
    BaseImporter(hostsService, vulnerabilitiesService, jobManager, jobsService, user, "Nessus Importer"), IVulnerabilityImporter, IJobRunner
{
    
    public override async Task Run()
    {
        await Task.Run(async () =>
        {
            //int importedVulnerabilities = 0;
        
            if (!File.Exists(_filePath)) throw new FileNotFoundException("File not found");
            
            NessusClientData_v2? nessusClientData = await NessusClientData_v2.ParseAsync(_filePath);
            
            if(nessusClientData == null) throw new Exception("Error parsing file");
            
            var reportHosts = new List<ReportHost>(nessusClientData.Report!.ReportHosts.Cast<ReportHost>());

            foreach (var hostReport in reportHosts)
            {
                TotalInteractions += hostReport.ReportItems.Count;
            }

            foreach (var host in reportHosts)
            {
                try
                {
                    CancellationTokenSource.Token.ThrowIfCancellationRequested();

                    string hostProperties = "";

                    foreach (var tag in host.HostProperties.Tags)
                    {
                        hostProperties += tag.Name + ":" + tag.Value + "\n";
                    }
                    
                    if (hostProperties.Length >  65000) hostProperties = hostProperties.Substring(0, 65000);

                    // First let´s check if the host already exists
                    var hostExists = await HostsService.HostExistsAsync(host.IpAddress);

                    Host nrHost;
                    if (hostExists)
                    {
                        nrHost = await HostsService.GetByIpAsync(host.IpAddress)!;
                        nrHost.LastVerificationDate = DateTime.Now;
                        nrHost.Status = (short)IntStatus.Active;
                        await HostsService.UpdateAsync(nrHost);
                    }
                    else
                    {
                        var maclen = host.MacAddress.Length > 254 ? 254 : host.MacAddress.Length;
                        nrHost = new Host()
                        {
                            Ip = host.IpAddress,
                            HostName = host.Name,
                            Fqdn = host.FQDN,
                            MacAddress = host.MacAddress.Substring(0,maclen),
                            Os = host.OS,
                            LastVerificationDate = DateTime.Now,
                            RegistrationDate = DateTime.Now,
                            Source = "Nessus",
                            Status = (short)IntStatus.Active,
                            TeamId = 2,
                            Comment = "Created by Nessus Importer",
                            Properties = hostProperties
                        };
                        var newHost = await HostsService.CreateAsync(nrHost);
                        nrHost = newHost!;
                    }

                    foreach (var item in host.ReportItems)
                    {

                        try
                        {
                            CancellationTokenSource.Token.ThrowIfCancellationRequested();

                            //Dealing with the service
                            var serviceExists =
                                await HostsService.HostHasServiceAsync(nrHost.Id, item.ServiceName, item.Port,
                                    item.Protocol);
                            DAL.Entities.HostsService? nrService;
                            if (!serviceExists)
                            {

                                var service = new DAL.Entities.HostsService()
                                {
                                    Name = item.ServiceName,
                                    Port = item.Port,
                                    Protocol = item.Protocol,
                                    HostId = nrHost.Id
                                };

                                nrService = await HostsService.CreateAndAddServiceAsync(nrHost.Id, service);
                            }
                            else
                            {
                                nrService = await HostsService.FindServiceAsync(nrHost.Id, s => s.Name == item.ServiceName
                                    && s.Port == item.Port && s.Protocol == item.Protocol);
                            }

                            if (_ignoreNegligible && item.Severity == "0")
                            {
                                continue;
                            }

                            var vulHashString = item.PluginName + nrHost.Id + item.Severity + item.RiskFactor +
                                                nrService!.Id;
                            var hash = HashTool.CreateSha1(vulHashString);

                            var vulnerabilityExists = false;
                            Vulnerability? vulFindResult = null;
                            try
                            {
                                vulFindResult = await VulnerabilitiesService.FindAsync(hash);
                                vulnerabilityExists = true;
                            }
                            catch (DataNotFoundException)
                            {
                                vulnerabilityExists = false;
                            }
                            catch (Exception ex)
                            {
                                Log.Error("Unkown error looking for vulnerability message:{Message}", ex.Message);
                                vulnerabilityExists = false;
                            }

                            
                            // if vulnerability does not exist we can do a deeper check
                            
                            // Only do that if service and host exists 
                            
                            if(serviceExists && hostExists && !vulnerabilityExists)
                            {
                                var vuls = await VulnerabilitiesService.GetVulnerabilitiesByHostIdAsync(nrHost.Id);
                                foreach (var vul in vuls)
                                {
                                    if(vul.Title == item.PluginName && vul.HostServiceId == nrService!.Id && vul.HostId == nrHost.Id)
                                    {
                                        vulnerabilityExists = true;
                                        vulFindResult = vul;
                                        break;
                                    }
                                }
                            }
                            

                            var action = new NrAction()
                            {
                                DateTime = DateTime.Now,
                                Message = "Created by Nessus Importer",
                                UserId = LoggedUser!.Value,
                                ObjectType = nameof(Vulnerability)

                            };
                            var userid = LoggedUser!.Value;
                            var cvestring = item.CVEs.Aggregate("", (current, cve) => current + cve + ",");

                            DateTime? vulnerabilityPublicationDate = null;
                            DateTime? patchPublicationDate = null;
                            if (item.VulnerabilityPublicationDate != null)
                            {
                                vulnerabilityPublicationDate = DateTime.ParseExact(item.VulnerabilityPublicationDate,
                                    "yyyy/MM/dd", CultureInfo.InvariantCulture);
                            }

                            if (item.PatchPublicationDate != null)
                            {
                                patchPublicationDate = DateTime.ParseExact(item.PatchPublicationDate, "yyyy/MM/dd",
                                    CultureInfo.InvariantCulture);
                            }

                            if (vulnerabilityExists)
                            {

                                //Vulnerability already exists
                                var vulnerability = vulFindResult!;
                                vulnerability.DetectionCount++;
                                vulnerability.LastDetection = DateTime.Now;
                                vulnerability.CvssTemporalScore = item.CVSSTemporalScore;
                                vulnerability.VulnerabilityPublicationDate = vulnerabilityPublicationDate;
                                vulnerability.PatchPublicationDate = patchPublicationDate;
                                vulnerability.ExploitAvaliable = item.ExploitAvailable;
                                vulnerability.ExploitabilityEasy = item.ExploitabilityEasy;
                                vulnerability.ExploitedByScanner = item.ExploitedByNessus;
                                vulnerability.ExploitCodeMaturity = item.ExploitCodeMaturity;
                                vulnerability.ThreatIntensity = item.ThreatIntensityLast28;
                                vulnerability.ThreatRecency = item.ThreatRecency;
                                vulnerability.ThreatSources = item.ThreatSourcesLast28;
                                vulnerability.Score = item.CVSS3BaseScore;
                                vulnerability.Cves = cvestring;
                                vulnerability.ImportHash = hash;
                                vulnerability.Cvss3Vector = item.CVSS3Vector;
                                vulnerability.Cvss3BaseScore = item.CVSS3BaseScore;
                                vulnerability.Cvss3ImpactScore = item.CVSS3ImpactScore;
                                vulnerability.Cvss3TemporalScore = item.CVSS3TemporalScore;
                                vulnerability.Cvss3TemporalVector = item.CVSS3TemporalVector;
                                vulnerability.CvssVector = item.CVSSVector;
                                vulnerability.CvssBaseScore = item.CVSSBaseScore;
                                vulnerability.CvssTemporalVector = item.CVSSTemporalVector;
                                vulnerability.CvssTemporalScore = item.CVSSTemporalScore;
                                vulnerability.VprScore = item.VPRScore;
                                vulnerability.Xref = item.Xref.Aggregate("", (current, xref) => current + xref + ",");
                                vulnerability.PatchPublicationDate = patchPublicationDate;
                                vulnerability.VulnerabilityPublicationDate = vulnerabilityPublicationDate;
                                vulnerability.ExploitAvaliable = item.ExploitAvailable;
                                vulnerability.ExploitabilityEasy = item.ExploitabilityEasy;
                                vulnerability.ExploitedByScanner = item.ExploitedByNessus;
                                vulnerability.ExploitCodeMaturity = item.ExploitCodeMaturity;
                                vulnerability.ThreatIntensity = item.ThreatIntensityLast28;
                                vulnerability.ThreatRecency = item.ThreatRecency;
                                vulnerability.ThreatSources = item.ThreatSourcesLast28;
                                

                                await VulnerabilitiesService.UpdateAsync(vulnerability);

                                action.Message = "Notified by Nessus Importer";

                                await VulnerabilitiesService.AddActionAsync(vulnerability.Id, userid, action);

                            }
                            else
                            {

                                var vulnerability = new Vulnerability
                                {
                                    Title = item.PluginName.Truncate(250)!,
                                    Description = item.Description.Truncate(65500),
                                    Severity = item.Severity, //ConvertCriticalityToInt(item.Criticality).ToString(), 
                                    Solution = item.Solution,
                                    Details = item.PluginOutput,
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
                                    AnalystId = LoggedUser.Value,
                                    Score = item.CVSS3BaseScore,
                                    Cves = cvestring,
                                    Cvss3Vector = item.CVSS3Vector,
                                    Cvss3BaseScore = item.CVSS3BaseScore,
                                    Cvss3ImpactScore = item.CVSS3ImpactScore,
                                    Cvss3TemporalScore = item.CVSS3TemporalScore,
                                    Cvss3TemporalVector = item.CVSS3TemporalVector,
                                    CvssVector = item.CVSSVector,
                                    CvssBaseScore = item.CVSSBaseScore,
                                    CvssTemporalVector = item.CVSSTemporalVector,
                                    CvssTemporalScore = item.CVSSTemporalScore,
                                    VulnerabilityPublicationDate = vulnerabilityPublicationDate,
                                    PatchPublicationDate = patchPublicationDate,
                                    ExploitAvaliable = item.ExploitAvailable,
                                    ExploitabilityEasy = item.ExploitabilityEasy,
                                    ExploitedByScanner = item.ExploitedByNessus,
                                    ExploitCodeMaturity = item.ExploitCodeMaturity,
                                    ThreatIntensity = item.ThreatIntensityLast28,
                                    ThreatRecency = item.ThreatRecency,
                                    ThreatSources = item.ThreatSourcesLast28,
                                    VprScore = item.VPRScore,
                                    Xref = item.Xref.Aggregate("", (current, xref) => current + xref + ",")

                                };
                                var vul = await VulnerabilitiesService.CreateAsync(vulnerability);
                                await VulnerabilitiesService.AddActionAsync(vul.Id, userid, action);
                                ImportedVulnerabilities++;
                            }
                            InteractionCompleted();
                        }
                        catch (Exception ex)
                        {
                            var finalMessage = ex.Message;
                            if(ex.InnerException != null)
                            {
                                finalMessage += " - " + ex.InnerException.Message;
                            }
                            
                            Error(finalMessage);
                            return;
                        }

                    }

                }
                catch (Exception ex)
                {
                    var finalMessage = ex.Message;
                    if(ex.InnerException != null)
                    {
                        finalMessage += " - " + ex.InnerException.Message;
                    }
                    Error(finalMessage);
                    return;
                }
            }

        }, CancellationTokenSource.Token);
        
        RegisterResult($"Imported {ImportedVulnerabilities} vulnerabilities");
        
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
    
}