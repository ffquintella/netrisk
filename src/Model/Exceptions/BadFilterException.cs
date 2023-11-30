namespace Model.Exceptions;

public class BadFilterException: Exception
{
    public string Filter { get; set; }
    public new string Message { get; set; }
    
    public BadFilterException(string filter, string message)
    {
        Filter = filter;
        Message = message;
    }
}