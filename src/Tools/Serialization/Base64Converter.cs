using System.Text;
using System.Text.Json;

namespace Tools.Serialization;


public static class Base64Converter
{
    public static T FromBase64Json<T>(string base64)
    {
        byte[] jsonBytes = Convert.FromBase64String(base64);
        string json = Encoding.UTF8.GetString(jsonBytes);
        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException();
    }
}