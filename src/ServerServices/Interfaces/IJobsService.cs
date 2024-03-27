namespace ServerServices.Interfaces;

public interface IJobsService
{
    public Task<int> RegisterJobAsync(string title);
    
    public Task RegisterJobStartAsync(int jobId, CancellationToken cancellationToken, int userId);
    
    public Task RegisterJobEndAsync(int jobId, string resultMessage);
    
    public Task UpdateJobProgressAsync(int jobId, int progress);
    
    public Task CancelJobAsync(int jobId);
    
}