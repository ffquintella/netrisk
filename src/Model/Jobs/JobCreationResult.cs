namespace Model.Jobs;

public class JobCreationResult
{
    public string Message { get; set; } = string.Empty;
    public int JobId { get; set; } = 0;
    public bool Success { get; set; } = false;
    public int JobStatus { get; set; } = 0;
}