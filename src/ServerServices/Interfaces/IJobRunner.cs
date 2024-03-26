namespace ServerServices.Interfaces;

public interface IJobRunner
{
    
    /// <summary>
    /// Runs the task
    /// </summary>
    /// <returns></returns>
    public Task Run();
    
    public void Cancel();
    
    public void Error(string message);
    
    public void RegisterProgress(int progress);
    
    public void RegisterResult(object result);
}