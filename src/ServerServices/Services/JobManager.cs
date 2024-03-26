using ServerServices.Interfaces;

namespace ServerServices.Services;

public class JobManager
{
    private readonly IJobsService _jobsService;

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
        await jobRunner.Run();

        // Register the end of the job
        return id;
    }
}