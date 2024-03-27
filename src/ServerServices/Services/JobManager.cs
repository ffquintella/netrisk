using Microsoft.AspNetCore.Authentication;
using ServerServices.Events;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class JobManager
{
    private readonly IJobsService _jobsService;
    
    private readonly IAuthenticationService _authenticationService;
    
    private List<int> _runningJobs = new();

    public JobManager(IJobsService jobsService)
    {
        _jobsService = jobsService;

    }


    //public async Task RunAndRegisterJob(Func<Task> job)
    public async Task<int> RunAndRegisterJob(IJobRunner jobRunner)
    {
        // Register the start of the job
        var id = await _jobsService.RegisterJobAsync(jobRunner.JobName);

        // Run the job
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        jobRunner.Run();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        
        jobRunner.StepCompleted += RegisterProgress;
        jobRunner.JobEnded += RegisterEndJob;
        
        // Register the beginning of the job
        await _jobsService.RegisterJobStartAsync(id, jobRunner.CancellationTokenSource.Token, jobRunner.LoggedUser!.Value);

        _runningJobs.Add(id);
        // Register the end of the job
        return id;
    }
    
    private async void RegisterProgress(object? sender, JobEventArgs eventArgs)
    {
        // Update the progress of the job
        await _jobsService.UpdateJobProgressAsync(eventArgs.JobId, eventArgs.PercentCompleted);
    }

    private async void RegisterEndJob(object? sender, JobEventArgs eventArgs)
    {
        await _jobsService.RegisterJobEndAsync(eventArgs.JobId, eventArgs.Message);
        _runningJobs.Remove(eventArgs.JobId);
    }
    
    public async Task CancelAllJobs()
    {
        foreach (var jobId in _runningJobs)
        {
            await CancelJob(jobId);
        }
    }
    
    public async Task CancelJob(int jobId)
    {
        // Cancel the job
        await _jobsService.CancelJobAsync(jobId);
        _runningJobs.Remove(jobId);
    }
}