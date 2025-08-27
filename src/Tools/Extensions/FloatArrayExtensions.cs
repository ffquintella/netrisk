namespace Tools.Extensions;

using System;

public static class FloatArrayExtensions
{
    /// <summary>
    /// Converts a float[] to a Base64-encoded string.
    /// </summary>
    public static string ToBase64(this float[] values)
    {
        if (values == null || values.Length == 0)
            return string.Empty;

        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    public static byte[] ToByteArray(this float[] source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        byte[] bytes = new byte[source.Length * sizeof(float)];
        Buffer.BlockCopy(source, 0, bytes, 0, bytes.Length);
        return bytes;
    }
    
    public static float[] ToFloatArray(this byte[] source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (source.Length % sizeof(float) != 0)
            throw new ArgumentException("Byte array length must be a multiple of 4.");

        float[] floats = new float[source.Length / sizeof(float)];
        Buffer.BlockCopy(source, 0, floats, 0, source.Length);
        return floats;
    }
    
    /// <summary>
    /// Converts a Base64-encoded string back to a float[].
    /// </summary>
    public static float[] FromBase64ToFloatArray(this string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return Array.Empty<float>();

        byte[] bytes = Convert.FromBase64String(base64);

        if (bytes.Length % sizeof(float) != 0)
            throw new FormatException("Invalid base64 input for a float array.");

        float[] result = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }
}
