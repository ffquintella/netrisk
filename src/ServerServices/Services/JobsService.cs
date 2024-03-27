using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
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

        var njob = await CreateJobAsync(job);
        
        return njob.Id;
    }

    public async Task RegisterJobStartAsync(int jobId, CancellationToken cancellationToken)
    {
        await using var dbContext = DalService.GetContext();
        
        var job = await dbContext.Jobs.FindAsync(jobId);
        if(job == null)
            throw new Exception("Job not found");

        //Serialize the job cancellationToken to a JSON string
        string jsonString = JsonSerializer.Serialize(job);
        
        // Convert the JSON string to a byte array
        byte[] jsonToken = System.Text.Encoding.UTF8.GetBytes(jsonString);

        job.CancellationToken = jsonToken;
        job.Status = (int)IntStatus.Running;
        
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateJobProgressAsync(int jobId, int progress)
    {
        await using var dbContext = DalService.GetContext();
        
        var job = await dbContext.Jobs.FindAsync(jobId);
        if(job == null)
            throw new Exception("Job not found");
        
        job.Progress = progress;
        
        await dbContext.SaveChangesAsync();
        
    }

    public async Task CancelJobAsync(int jobId)
    {
        await using var dbContext = DalService.GetContext();
        
        var job = await dbContext.Jobs.FindAsync(jobId);
        if(job == null)
            throw new Exception("Job not found");
        
        job.Status = (int)IntStatus.Cancelled;
        job.Result = "Job was cancelled"u8.ToArray();
        
        await dbContext.SaveChangesAsync();
    }
    
    private async Task<Job> CreateJobAsync(Job job)
    {
        await using var dbContext = DalService.GetContext();
        
        if(job.Id != 0)
            throw new Exception("Job already exists");
        
        await dbContext.Jobs.AddAsync(job);
        await dbContext.SaveChangesAsync();
        return job;

    }
}