using ClientServices.Events;
using Splat;
using ILogger = Serilog.ILogger;

namespace ClientServices.Services.Importers;

public class BaseImporter
{
    protected CancellationTokenSource cts = new CancellationTokenSource();
    
    protected ILogger Logger { get; } = GetService<ILogger>();
    
    protected int TotalInteractions { get; set; } = 0;
    protected int InteractionIncrement { get; set; } = 0;
    protected int InteractionsCompleted { get; set; } = 0;
    protected int ImportedVulnerabilities { get; set; } = 0;

    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
    
    public event EventHandler<ProgressBarrEventArgs>? StepCompleted;
    
    private void NotifyStepCompleted(ProgressBarrEventArgs pc)
    {
        EventHandler<ProgressBarrEventArgs>? handler = StepCompleted;
        if (handler != null) handler(this, pc);
    }
    
    protected void InteractionCompleted()
    {
        InteractionsCompleted++;
        if(InteractionsCompleted == 0) return;
        if(TotalInteractions == 0) return;
        if (InteractionsCompleted % InteractionIncrement == 0)
        {
            var progress = (((double)InteractionsCompleted / (double)TotalInteractions) * 100);
            
            var pc = new ProgressBarrEventArgs {Progess = (int)progress};
            NotifyStepCompleted(pc);
        }
    }
    
    public void Cancel()
    {
        cts.Cancel();
    }
}