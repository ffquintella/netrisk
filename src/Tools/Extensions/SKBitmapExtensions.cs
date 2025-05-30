namespace Tools.Extensions;

using SkiaSharp;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class SKBitmapExtensions
{
    /// <summary>
    /// Serializes the SKBitmap to a Base64-encoded PNG string.
    /// </summary>
    public static string ToBase64Png(this SKBitmap bitmap)
    {
        if (bitmap == null)
            throw new ArgumentNullException(nameof(bitmap));

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100); // 100 = max quality
        return Convert.ToBase64String(data.ToArray());
    }

    /// <summary>
    /// Deserializes a Base64-encoded PNG string back into an SKBitmap.
    /// </summary>
    public static SKBitmap FromBase64Png(this string base64Png)
    {
        if (string.IsNullOrWhiteSpace(base64Png))
            throw new ArgumentException("Base64 string is null or empty.", nameof(base64Png));

        byte[] imageBytes = Convert.FromBase64String(base64Png);

        using var data = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(data);

        var info = codec.Info;
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());

        if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
            return bitmap;

        bitmap.Dispose();
        throw new InvalidOperationException($"Failed to decode SKBitmap. Codec result: {result}");
    }

    /// <summary>
    /// Optionally: wraps the base64 PNG string into a JSON property
    /// </summary>
    public static string ToJson(this SKBitmap bitmap)
    {
        var base64 = bitmap.ToBase64Png();
        return JsonSerializer.Serialize(new { ImageBase64 = base64 });
    }

    /// <summary>
    /// Deserializes an SKBitmap from a JSON string containing "ImageBase64".
    /// </summary>
    public static SKBitmap FromJson(this string json)
    {
        var wrapper = JsonSerializer.Deserialize<ImageWrapper>(json);
        return wrapper.ImageBase64.FromBase64Png();
    }

    private class ImageWrapper
    {
        [JsonPropertyName("ImageBase64")]
        public string ImageBase64 { get; set; }
    }
}
