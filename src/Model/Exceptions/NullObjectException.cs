namespace Model.Exceptions;

public class NullObjectException: Exception
{
    
    public string ObjectName { get; }
    
    public NullObjectException(string objectName) : base($"The object '{objectName}' is null.")
    {
        ObjectName = objectName;
    }
    
    public NullObjectException(string message, Exception innerException) : base(message, innerException)
    {
    }

    
}