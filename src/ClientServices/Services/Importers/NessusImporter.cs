using ClientServices.Events;
using ClientServices.Interfaces.Importers;

namespace ClientServices.Services.Importers;

public class NessusImporter: IVulnerabilityImporter
{
    public int Import(string filePath)
    {
        int importedVulnerabilities = 0;


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