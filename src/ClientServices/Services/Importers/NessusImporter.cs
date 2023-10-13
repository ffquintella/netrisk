using System.Xml.Linq;
using ClientServices.Events;
using ClientServices.Interfaces.Importers;

namespace ClientServices.Services.Importers;

public class NessusImporter: IVulnerabilityImporter
{
    public async Task<int> Import(string filePath)
    {
        int importedVulnerabilities = 0;

        if (!File.Exists(filePath)) throw new FileNotFoundException("File not found");
        
        var data = File.ReadAllText(filePath);
        
        XDocument doc = XDocument.Parse(data);
        Dictionary<string, string> dataDictionary = new Dictionary<string, string>();

        var descendants = doc.Descendants().Where(p => p.HasElements == false);

        var tic = descendants.Count() / 100;

        int interactions = 0;

        await Task.Run(() =>
        {
            foreach (XElement element in descendants) {
                int keyInt = 0;
                string keyName = element.Name.LocalName;

                while (dataDictionary.ContainsKey(keyName)) {
                    keyName = element.Name.LocalName + "_" + keyInt++;
                }

                dataDictionary.Add(keyName, element.Value);
                interactions++;
                if(interactions % tic == 0)
                    NotifyStepCompleted(new ProgressBarrEventArgs{Progess = interactions / tic});
            }
        });


        var bc = dataDictionary;
        

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