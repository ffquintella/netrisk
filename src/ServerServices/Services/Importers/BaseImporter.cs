﻿using DAL.Entities;
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
    
    protected void InteractionCompleted()
    {
        InteractionsCompleted++;
        if(InteractionsCompleted == 0) return;
        if(TotalInteractions == 0) return;
        
        var interactionIncrement = Math.Floor((double)TotalInteractions / 100);
        
        if (InteractionsCompleted % interactionIncrement == 0)
        {
            _progress++;
            var pc = new JobEventArgs
            {
                PercentCompleted = _progress,
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