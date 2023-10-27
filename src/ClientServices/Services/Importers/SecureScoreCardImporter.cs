using System.Globalization;
using System.Text.RegularExpressions;
using ClientServices.Events;
using ClientServices.Interfaces;
using ClientServices.Interfaces.Importers;
using CsvHelper;
using CsvHelper.Configuration;
using Tools.Math;

namespace ClientServices.Services.Importers;

public class SecureScoreCardImporter: BaseImporter, IVulnerabilityImporter
{
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    
    public async Task<int> Import(string filePath, bool ignoreNegligible = true)
    {
        int importedVulnerabilities = 0;
        int interactions = 0;

        if (!File.Exists(filePath)) throw new FileNotFoundException("File not found");

        await Task.Run(() =>
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
                    
                    //importedVulnerabilities++;
                    
                    if (ignoreNegligible && record.IssueTypeSeverity == "INFO")
                    {
                        interactions++;
                        
                        var rest = Convert.ToInt32(interactions % tic);
                        if (rest == 0) CompleteStep();
                        continue;
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
}