namespace Model.Exceptions;

public class InvalidReferenceException: DatabaseException
{
    public InvalidReferenceException(string message) : base(message)
    {
    }
}

