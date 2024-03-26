namespace ServerServices.Interfaces;

public interface IJobsService
{
    public Task<int> RegisterRunningJobAsync();
}