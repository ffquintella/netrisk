using Avalonia;
using Avalonia.Platform;
using SkiaSharp;
using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace GUIClient.Tools;

public static class GUIImageTools
{
    public static SKImage? LoadSkImageFromAvares(string uriString)
    {
        var uri = new Uri(uriString);

        using var stream = AssetLoader.Open(uri);
        using var skStream = new SKManagedStream(stream);
        using var data = SKData.Create(skStream);
        return SKImage.FromEncodedData(data);
    }

    public static void SaveBitmapAsPng(Bitmap bitmap, string filePath)
    {
        try
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Create))
            {
                bitmap.Save(stream);
            }
        }
        catch (Exception ex)
        {
            throw new Exception("Error saving image", ex);
        }
    }

    public static void SaveBitmapArrayAsPng(byte[] bitmap, string outputPath)
    {
        try
        {
            File.WriteAllBytes(outputPath, bitmap);
        }
        catch (Exception ex)
        {
            throw new Exception("Error saving image to stream", ex);
        }
    }

    public static byte[] ToPngByteArray(this Bitmap bitmap)
    {
        if (bitmap == null)
        {
            return null;
        }

        using (var memoryStream = new MemoryStream())
        {
            bitmap.Save(memoryStream);

            return memoryStream.ToArray();
        }
    }
}
