
namespace Model.Exceptions;

public class InvalidHttpRequestException: Exception
{
    public String Url { get; set; }
    public String Method { get; set; }
    public InvalidHttpRequestException(string message, string url, string method) : base(message)
    {
        Url = url;
        Method = method;
    }
}