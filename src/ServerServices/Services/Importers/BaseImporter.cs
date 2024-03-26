using DAL.Entities;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services.Importers;

public class BaseImporter(IHostsService hostsService, 
    IVulnerabilitiesService vulnerabilitiesService, 
    JobManager jobManager,
    IJobsService jobsService,  
    User? user)
{

    protected IHostsService HostsService { get; } = hostsService;
    protected IVulnerabilitiesService VulnerabilitiesService { get; } = vulnerabilitiesService;
    
    protected IJobsService JobsService { get; } = jobsService;
    
    protected JobManager JobManager { get; } = jobManager;
    
    protected CancellationTokenSource cts = new CancellationTokenSource();
    protected int TotalInteractions { get; set; } = 0;
    protected int InteractionIncrement { get; set; } = 0;
    protected int InteractionsCompleted { get; set; } = 0;
    protected int ImportedVulnerabilities { get; set; } = 0;
    protected User? LoggedUser { get; set; } = user;


    /*
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
            var pc = new ProgressBarrEventArgs {Progess = InteractionsCompleted / TotalInteractions};
            NotifyStepCompleted(pc);
        }
    }*/
    
    public void Cancel()
    {
        cts.Cancel();
    }
}