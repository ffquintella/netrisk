using Microsoft.Extensions.Localization;
using Model.Messages;
using ServerServices.Events;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class JobManager : IJobManager
{
    private readonly IJobsService _jobsService;

    private readonly IMessagesService _messagesService;
    
    private IStringLocalizer Localizer { get; }
    
    private List<int> _runningJobs = new();

    /// <summary>
    /// The unused <c>IAuthenticationService</c> parameter this constructor used to take made the API
    /// refuse to start in the Development environment.
    ///
    /// <c>JobManager</c> is registered as a singleton and <c>IAuthenticationService</c> is scoped, so
    /// ASP.NET Core's scope validation — which only runs in Development — rejected the graph before
    /// the host came up. The parameter was never used: the field it was meant for is commented out
    /// two lines above where it was assigned. Found while standing the stack up to verify the Track 8
    /// risk portal end to end.
    /// </summary>
    public JobManager(IJobsService jobsService, IMessagesService messagesService,
        ILocalizationService localizationService)
    {
        _jobsService = jobsService;
        _messagesService = messagesService;

        Localizer = localizationService.GetLocalizer();
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
        jobRunner.JobFailed += RegisterFailedJob;
        
        // Register the beginning of the job
        await _jobsService.RegisterJobStartAsync(id, jobRunner.CancellationTokenSource.Token, jobRunner.LoggedUser!.Value);

        _runningJobs.Add(id);
        
        await _messagesService.SendMessageAsync(Localizer["JobStartedMSG"] + jobRunner.JobName , jobRunner.LoggedUser!.Value, (int)ChatTypes.Jobs);
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
        // Get job details
        var jobRunner = (IJobRunner) sender!;
        
        await _jobsService.RegisterJobEndAsync(eventArgs.JobId, eventArgs.Message);
        _runningJobs.Remove(eventArgs.JobId);
        await _messagesService.SendMessageAsync(Localizer["JobEndMSG"] + jobRunner.JobName , jobRunner.LoggedUser!.Value, (int)ChatTypes.Jobs);
    }
    
    private async void RegisterFailedJob(object? sender, JobEventArgs eventArgs)
    {
        // Get job details
        var jobRunner = (IJobRunner) sender!;
        
        await _jobsService.RegisterJobFailedAsync(eventArgs.JobId, eventArgs.Message);
        _runningJobs.Remove(eventArgs.JobId);
        
        await _messagesService.SendMessageAsync(Localizer["JobFailedMSG"] + jobRunner.JobName , jobRunner.LoggedUser!.Value, (int)ChatTypes.Jobs);
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