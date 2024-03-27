namespace ServerServices.Interfaces;

public interface IJobsService
{
    public Task<int> RegisterRunningJobAsync();
    
    public Task RegisterJobStartAsync(int jobId, CancellationToken cancellationToken);
    
    
    public Task UpdateJobProgressAsync(int jobId, int progress);
    
    public Task CancelJobAsync(int jobId);
    
}