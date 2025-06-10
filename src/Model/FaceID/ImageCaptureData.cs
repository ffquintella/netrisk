namespace Model.FaceID;

public class ImageCaptureData
{
    public int UserId { get; set; } = 1000;
    public int CaptureSequenceIndex { get; set; } = 0;
    public byte[] PngImageData { get; set; } = Array.Empty<byte>();
    public char CaptureImageLight { get; set; } = 'O';

}