using System.Globalization;
using System.Text.RegularExpressions;
using ClientServices.Events;
using ClientServices.Interfaces;
using ClientServices.Interfaces.Importers;
using CsvHelper;
using CsvHelper.Configuration;
using DAL.Entities;
using Model;
using Model.DTO;
using Tools.Math;
using Tools.Security;

namespace ClientServices.Services.Importers;

public class SecureScoreCardImporter: BaseImporter, IVulnerabilityImporter
{
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    private IAuthenticationService AuthenticationService { get; } = GetService<IAuthenticationService>();
    private IHostsService HostsService { get; } = GetService<IHostsService>();
    
    
    public async Task<int> Import(string filePath, bool ignoreNegligible = true)
    {
        int importedVulnerabilities = 0;
        int interactions = 0;

        if (!File.Exists(filePath)) throw new FileNotFoundException("File not found");

        await  Task.Run( async () =>
        {
            
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                PrepareHeaderForMatch = args =>
                {
                    var result = args.Header.ToLower();
                    result = result.Trim();
                    result = Regex.Replace(result, "[^a-z0-9]", String.Empty);
                    //result = Regex.Replace(result, @"[^\u0000-\u007F]+", string.Empty);
                    return result;
                },
            };
            
            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, config))
            {
                //csv.Context.RegisterClassMap<FooMap>();
                var records = csv.GetRecords<SSCVulnerability>().ToList();
                var recordNumber = records.Count();

                var tic = DivisionHelper.RoundedDivision(recordNumber, 100);

                foreach (var record in records)
                {
                    
                    if (record.IssueTypeSeverity == "POSITIVE")
                    {
                        interactions++;
                        
                        var rest = Convert.ToInt32(interactions % tic);
                        if (rest == 0) CompleteStep();
                        continue;
                    }
                    
                    if (ignoreNegligible && record.IssueTypeSeverity == "INFO")
                    {
                        interactions++;
                        
                        var rest = Convert.ToInt32(interactions % tic);
                        if (rest == 0) CompleteStep();
                        continue;
                    }
                    
                    // let´s check if we have a specific host here or not
                    Host rHost = new Host();
                    HostsService rService = new HostsService();
                    
                    if (record.Hostname != "" && record.IpAddresses != "")
                    {
                        
                        // now let´s check if this host already exists 
                        var hostExists = HostsService.HostExists(record.IpAddresses);

                        if (hostExists)
                        {
                            rHost = HostsService.GetByIp(record.IpAddresses)!;
                            rHost.Fqdn = record.Target;
                            rHost.LastVerificationDate = DateTime.Now;
                            if(record.Status == "active") rHost.Status = (short) IntStatus.Active;
                            else rHost.Status = (short) IntStatus.Retired;
                            HostsService.Update(rHost);
                            if (record.Ports != "")
                            {
                                var serviceExists = await HostsService.HostHasService(rHost.Id, "TCP-" + record.Ports, 
                                    Int32.Parse(record.Ports), "tcp");
                                
                                if(!serviceExists)
                                {

                                    var hostService = new HostsServiceDto()
                                    {
                                        Id = rHost.Id,
                                        Name = "TCP-" + record.Ports,
                                        Port = Int32.Parse(record.Ports),
                                        Protocol = "tcp",
                                    };
                                    
                                    rService = await HostsService.CreateAndAddService(rHost.Id, hostService);
                                    
                                }
                            }
                        }
                        else
                        {
                            rHost = new Host()
                            {
                                Ip = record.IpAddresses,
                                HostName = record.Hostname,
                                Fqdn = record.Target,
                                LastVerificationDate = DateTime.Now,
                                Source = "SecureScoreCard",
                                RegistrationDate = DateTime.Now,
                                Status = (short) IntStatus.Active,
                                TeamId = 2,
                                Comment = "Created by SecureScoreCard Importer",
                            };
                            

                            if (record.Status == "active") rHost.Status = (short) IntStatus.Active;
                            else rHost.Status = (short) IntStatus.Retired;
                            
                            var newHost = await HostsService.Create(rHost);
                            if(newHost != null) rHost = newHost;

                            if (record.Ports != "")
                            {
                                var hostService = new HostsServiceDto()
                                {
                                    Id = rHost.Id,
                                    Name = "TCP-" + record.Ports,
                                    Port = Int32.Parse(record.Ports),
                                    Protocol = "tcp",
                                    
                                };

                                rService = await HostsService.CreateAndAddService(rHost.Id, hostService);
                            }
                        }

                    }
                    
                    // now let´s check if this vulnerability already exists
                    
                    var vulHashString = record.IssueId;
                    var hash = HashTool.CreateSha1(vulHashString);

                    var vulFindResult = await VulnerabilitiesService.Find(hash);
                    
                    var action = new NrAction()
                    {
                        DateTime = DateTime.Now,
                        Message = "Created by SecureScoreCard Importer",
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

                        action.Message = "Notified by SecureScoreCard Importer";
                        
                        await VulnerabilitiesService.AddAction(vulnerability.Id, userid, action);

                    }
                    else
                    {

                        int? hid;
                        int? hsid;
                        if (rHost.Id != 0) hid = rHost.Id;
                        else hid = null;
                        if(rService.Id != 0) hsid = rService.Id;
                        else hsid = null;

                        var score = Convert.ToInt32(Single.Parse(record.IssueTypeScoreImpactInScoring30));
                        
                        var vulnerability = new Vulnerability
                        {
                            Title = record.Label + "-" + record.IssueTypeTitle,
                            Description = record.Description +"\r***\r" + record.FactorName + "\r----\r" + record.IssueTypeTitle + "\r" + record.Analysis +"\r" + record.FinalUrl ,
                            Severity = ConvertSeverityToInt(record.IssueTypeSeverity).ToString(),
                            Solution = record.IssueRecommendation,
                            Details = record.FactorName + "\r---\r" + record.IssueTypeTitle + "\r###\r Browser: " 
                                      + record.Browser + "\r URL:" + record.FinalUrl + "\r Malware:" + record.MalwareType  + "\r Data:" + record.Data,
                            DetectionCount = 1,
                            LastDetection = DateTime.Now,
                            FirstDetection = DateTime.Now,
                            Status = (ushort) IntStatus.New,
                            HostId = hid,
                            FixTeamId = 1,
                            Technology = record.Product,
                            ImportSorce = "secureScoreCard",
                            HostServiceId = hsid,
                            ImportHash = hash,
                            AnalystId = AuthenticationService.AuthenticatedUserInfo!.UserId,
                            Score = score,

                        };
                        var vul = await VulnerabilitiesService.Create(vulnerability);
                        await VulnerabilitiesService.AddAction(vul.Id, userid, action);
                    }
                    
                    
                    interactions++;
                    var rest2 = Convert.ToInt32(interactions % tic);
                    if (rest2 == 0) CompleteStep();
                    
                }
            }
        });



        return importedVulnerabilities;
    }

    public event EventHandler<ProgressBarrEventArgs>? StepCompleted;
    
    private void NotifyStepCompleted(ProgressBarrEventArgs pc)
    {
        EventHandler<ProgressBarrEventArgs>? handler = StepCompleted;
        if (handler != null) handler(this, pc);
    }
    
    private void CompleteStep()
    {
        var pc = new ProgressBarrEventArgs {Progess = 1};
        NotifyStepCompleted(pc);
    }
    
    public int ConvertSeverityToInt(string severity)
    {
        switch(severity)
        {
            case "INFO":
                return 0;
            case "LOW":
                return 1;
            case "MEDIUM":
                return 3;
            case "HIGH":
                return 4;
            case "POSITIVE":
                return 0;
            default:
                return 0;
        }
    }
}