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
