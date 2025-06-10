using Avalonia;
using Avalonia.Platform;
using SkiaSharp;
using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Splat;
using Avalonia.Media.Imaging;

namespace GUIClient.Tools;

public static class ImageTools
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
            // Crie um FileStream para o arquivo de saída
            using (FileStream stream = new FileStream(filePath, FileMode.Create))
            {
                // Salve o bitmap no stream. Avalonia salva como PNG por padrão.
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
            // Salve o bitmap no stream. Avalonia salva como PNG por padrão.
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
            // Avalonia's Bitmap class has a Save method.
            // You can specify the format and quality if needed.
            // For PNG, the quality parameter is often ignored or has little effect as PNG is lossless.
            bitmap.Save(memoryStream); 
            
            return memoryStream.ToArray();
        }
    }
    
}