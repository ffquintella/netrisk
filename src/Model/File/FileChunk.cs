namespace Model.File;

public class FileChunk
{
    public int ChunkNumber { get; set; }
    public int TotalChunks { get; set; }
    public string FileId { get; set; } = string.Empty;
    
    public string ChunkData { get; set; } = string.Empty;
}