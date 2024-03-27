using DAL.Entities;
using ServerServices.Events;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services.Importers;

public abstract class BaseImporter(IHostsService hostsService, 
    IVulnerabilitiesService vulnerabilitiesService, 
    JobManager jobManager,
    IJobsService jobsService,  
    User? user): IJobRunner
{

    protected string _filePath = string.Empty;
    protected bool _ignoreNegligible = true;
    
    protected IHostsService HostsService { get; } = hostsService;
    protected IVulnerabilitiesService VulnerabilitiesService { get; } = vulnerabilitiesService;
    
    protected IJobsService JobsService { get; } = jobsService;
    
    protected JobManager JobManager { get; } = jobManager;
    
    public CancellationTokenSource CancellationTokenSource { get;  } = new ();
    protected int TotalInteractions { get; set; } = 0;
    //protected int InteractionIncrement { get; set; } = 0;
    private int InteractionsCompleted { get; set; } = 0;
    protected int ImportedVulnerabilities { get; set; } = 0;
    protected User? LoggedUser { get; set; } = user;
    protected int JobId = 0;
    private int Progress = 0;
    public event EventHandler<JobEventArgs>? StepCompleted;
    
    private void NotifyStepCompleted(JobEventArgs pc)
    {
        EventHandler<JobEventArgs>? handler = StepCompleted;
        if (handler != null) handler(this, pc);
    }
    
    protected void InteractionCompleted()
    {
        InteractionsCompleted++;
        if(InteractionsCompleted == 0) return;
        if(TotalInteractions == 0) return;
        
        var interactionIncrement = Math.Floor((double)TotalInteractions / 100);
        
        if (InteractionsCompleted % interactionIncrement == 0)
        {
            Progress++;
            var pc = new JobEventArgs
            {
                PercentCompleted = Progress,
                JobId = JobId
            };
            NotifyStepCompleted(pc);
        }
    }
    
    public void Error(string message)
    {
        throw new NotImplementedException();
    }

    public void RegisterProgress(int progress)
    {
        throw new NotImplementedException();
    }

    public void RegisterResult(object result)
    {
        throw new NotImplementedException();
    }

    public abstract Task Run();

    public async Task<int> Import(string filePath, bool ignoreNegligible = true)
    {
        _filePath = filePath;
        _ignoreNegligible = ignoreNegligible;
        
        JobId = await JobManager.RunAndRegisterJob(this);
        return JobId;
    }
    
    public void Cancel()
    {
        CancellationTokenSource.Cancel();
    }
}