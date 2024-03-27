namespace ServerServices.Events;

public class JobEventArgs
{
    public int PercentCompleted { get; set; } = 0;
    public int JobId { get; set; } = 0;
    public string Message { get; set; } = string.Empty;
}