namespace Model.FaceID;

public class FaceData
{
    public int UserId { get; set; }
    public string? ImageType { get; set; }
    public string? FaceImageB64 { get; set; }
    public string? FaceDescriptorB64 { get; set; }
    
}