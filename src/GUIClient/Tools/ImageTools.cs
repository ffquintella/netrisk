using Avalonia;
using Avalonia.Platform;
using SkiaSharp;
using System;
using Microsoft.Extensions.DependencyInjection;
using Splat;

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
}