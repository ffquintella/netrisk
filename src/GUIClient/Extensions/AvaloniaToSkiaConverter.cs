using System.IO;
using Avalonia.Media.Imaging;   // Avalonia bitmap API
using SkiaSharp;   

namespace GUIClient.Extensions;

public static class AvaloniaToSkiaConverter
{
    /// <summary>
    /// Converts an Avalonia Bitmap to a SkiaSharp SKImage via in-memory PNG encoding.
    /// </summary>
    /// <param name="avaloniaBitmap">The Avalonia Bitmap to convert.</param>
    /// <returns>An SKImage, or null if conversion fails.</returns>
    public static SKImage? ToSKImage(this Bitmap avaloniaBitmap)
    {
        using var memStream = new MemoryStream();
        avaloniaBitmap.Save(memStream); // Default is PNG
        memStream.Position = 0;
        using var skData = SKData.Create(memStream);
        return SKImage.FromEncodedData(skData);
    }

    /// <summary>
    /// Converts an Avalonia Bitmap to a SkiaSharp SKBitmap via in-memory PNG encoding.
    /// </summary>
    /// <param name="avaloniaBitmap">The Avalonia Bitmap to convert.</param>
    /// <returns>An SKBitmap, or null if conversion fails.</returns>
    public static SKBitmap? ToSKBitmap(this Bitmap avaloniaBitmap)
    {
        using var memStream = new MemoryStream();
        avaloniaBitmap.Save(memStream); // Default is PNG
        memStream.Position = 0;
        using var skData = SKData.Create(memStream);
        var skBitmap = SKBitmap.Decode(skData);
        return skBitmap;
    }
}