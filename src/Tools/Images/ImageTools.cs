using SkiaSharp;
using System.IO;

namespace Tools.Images;

public class ImageTools
{
    public static SKImage LoadImageFromBytes(byte[] pngData)
    {
        using var stream = new MemoryStream(pngData);
        using var skStream = new SKManagedStream(stream);
        return SKImage.FromEncodedData(skStream);
    }
    
    public static SKBitmap? LoadBitmapFromPngBytes(byte[] pngData)
    {
        var bitmap = SKBitmap.Decode(pngData);
        return bitmap ?? throw new InvalidDataException("Invalid or corrupt image data.");
    }

}