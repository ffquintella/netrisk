namespace Model.Exceptions;

public class DataProcessingException(
    string className,
    string method,
    string message,
    Exception? innerException = null)
    : Exception($"Error processing in class {className} method {method} message {message}", innerException)
{
    
    public String ClassName { get; } = className;
    public String Method { get; } = method;
    public override String Message { get; } = message;
}