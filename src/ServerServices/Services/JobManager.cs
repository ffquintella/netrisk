using ServerServices.Events;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class JobManager
{
    private readonly IJobsService _jobsService;
    
    private List<int> _runningJobs = new();

    public JobManager(IJobsService jobsService)
    {
        _jobsService = jobsService;
    }

    //public async Task RunAndRegisterJob(Func<Task> job)
    public async Task<int> RunAndRegisterJob(IJobRunner jobRunner)
    {
        // Register the start of the job
        var id = await _jobsService.RegisterRunningJobAsync();

        // Run the job
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        jobRunner.Run();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        
        jobRunner.StepCompleted += RegisterProgress;
        
        // Register the beginning of the job
        await _jobsService.RegisterJobStartAsync(id, jobRunner.CancellationTokenSource.Token);

        _runningJobs.Add(id);
        // Register the end of the job
        return id;
    }
    
    private async void RegisterProgress(object? sender, JobEventArgs eventArgs)
    {
        // Update the progress of the job
        await _jobsService.UpdateJobProgressAsync(eventArgs.JobId, eventArgs.PercentCompleted);
    }
    
    public async Task CancelJob(int jobId)
    {
        // Cancel the job
        await _jobsService.CancelJobAsync(jobId);
        _runningJobs.Remove(jobId);
    }
}