namespace Model.DTO;

public class FileListing
{
    public string Name { get; set; } = "";
    public string UniqueName { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int OwnerId { get; set; }
}