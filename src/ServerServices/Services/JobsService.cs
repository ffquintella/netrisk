using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
using DAL.Entities;
using Microsoft.AspNetCore.Authentication;
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
    
    public async Task<int> RegisterJobAsync(string title)
    {
        var job = new Job
        {
            Status = (int)IntStatus.New,
            Progress = 0,
            Title = title,
            LastUpdate = DateTime.Now
        };

        var njob = await CreateJobAsync(job);
        
        return njob.Id;
    }

    public async Task RegisterJobStartAsync(int jobId, CancellationToken cancellationToken, int userId)
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
        job.OwnerId = userId;
        job.StartedAt = DateTime.Now;
        
        await dbContext.SaveChangesAsync();
    }

    public async Task RegisterJobEndAsync(int jobId, string resultMessage)
    {
        await using var dbContext = DalService.GetContext();
        
        var job = await dbContext.Jobs.FindAsync(jobId);
        if(job == null)
            throw new Exception("Job not found");
        

        job.CancellationToken = null;
        job.Status = (int)IntStatus.Completed;
        job.Result = System.Text.Encoding.UTF8.GetBytes(resultMessage);
        job.LastUpdate = DateTime.Now;
        job.Progress = 100;
        
        await dbContext.SaveChangesAsync();
    }

    public async Task UpdateJobProgressAsync(int jobId, int progress)
    {
        await using var dbContext = DalService.GetContext();
        
        var job = await dbContext.Jobs.FindAsync(jobId);
        if(job == null)
            throw new Exception("Job not found");
        
        job.Progress = progress;
        job.LastUpdate = DateTime.Now;
        
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
        job.LastUpdate = DateTime.Now;
        
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