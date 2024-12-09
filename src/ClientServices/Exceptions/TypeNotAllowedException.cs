namespace ClientServices.Exceptions;

public class TypeNotAllowedException: Exception
{
    public TypeNotAllowedException(string message): base(message)
    {
    }
}