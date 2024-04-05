using DAL.Entities;
using Serilog;
using ServerServices.Events;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services.Importers;

public abstract class BaseImporter(IHostsService hostsService, 
    IVulnerabilitiesService vulnerabilitiesService, 
    JobManager jobManager,
    IJobsService jobsService,
    User? user,
    string jobName): IJobRunner
{

    protected string _filePath = string.Empty;
    protected bool _ignoreNegligible = true;
    protected IHostsService HostsService { get; } = hostsService;
    protected IVulnerabilitiesService VulnerabilitiesService { get; } = vulnerabilitiesService;
    protected IJobsService JobsService { get; } = jobsService;
    private JobManager JobManager { get; } = jobManager;
    public string JobName { get; } = jobName;
    public CancellationTokenSource CancellationTokenSource { get;  } = new ();
    protected int TotalInteractions { get; set; } = 0;
    private int InteractionsCompleted { get; set; } = 0;
    protected int ImportedVulnerabilities { get; set; } = 0;
    public User? LoggedUser { get; set; } = user;
    protected int JobId = 0;
    private int _progress = 0;
    public event EventHandler<JobEventArgs>? StepCompleted;
    public event EventHandler<JobEventArgs>? JobEnded;
    public event EventHandler<JobEventArgs>? JobFailed;
    
    private void NotifyStepCompleted(JobEventArgs pc)
    {
        EventHandler<JobEventArgs>? handler = StepCompleted;
        if (handler != null) handler(this, pc);
    }
    
    protected void NotifyJobEnded(JobEventArgs pc)
    {
        EventHandler<JobEventArgs>? handler = JobEnded;
        if (handler != null) handler(this, pc);
    }
    
    protected void NotifyJobFailed(JobEventArgs pc)
    {
        EventHandler<JobEventArgs>? handler = JobFailed;
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
            _progress++;
            RegisterProgress(_progress);
        }
    }
    
    public void Error(string message)
    {
        var pc = new JobEventArgs
        {
            PercentCompleted = 100,
            JobId = JobId,
            Message = message
        };
        Log.Error("Error in job {JobName} - {Message}", JobName, message);
        NotifyJobFailed(pc);
    }

    public void RegisterProgress(int progress)
    {
        var pc = new JobEventArgs
        {
            PercentCompleted = progress,
            JobId = JobId,
        };
        Log.Information("Job {JobName} - {Id} is {progress} completed", JobName, JobId, progress);
        NotifyStepCompleted(pc);
    }

    public void RegisterResult(string result)
    {
        var pc = new JobEventArgs
        {
            PercentCompleted = 100,
            JobId = JobId,
            Message = result
        };
        Log.Information("Job {JobName} finished - {Message}", JobName, result);
        NotifyJobEnded(pc);
        
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