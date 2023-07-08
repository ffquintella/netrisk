using Model.Rest;

namespace Model.Exceptions;

public class ErrorSavingException: Exception
{
    public OperationError Result { get; set; }
    public ErrorSavingException(string message, OperationError result) : base(message)
    {
        Result = result;
    }
}