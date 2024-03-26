using DAL.Entities;
using Model;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class JobsService: ServiceBase, IJobsService
{

    public JobsService(ILogger logger, DALService dalService
    ) : base(logger, dalService)
    {
        
    }
    
    public async Task<int> RegisterRunningJobAsync()
    {
        var job = new Job
        {
            Status = (int)IntStatus.New
        };

        var njob = await CreateJob(job);
        
        return njob.Id;
    }
    
    public async Task<Job> CreateJob(Job job)
    {
        await using var dbContext = DalService.GetContext();
        
        if(job.Id != 0)
            throw new Exception("Job already exists");
        
        await dbContext.Jobs.AddAsync(job);
        await dbContext.SaveChangesAsync();
        return job;

    }
}