namespace Model.Database;

public class DatabaseOperationResult
{
    public string Status { get; set; } = "Error";
    public int Code { get; set; } = 0;
    public string Message { get; set; } = "";
}