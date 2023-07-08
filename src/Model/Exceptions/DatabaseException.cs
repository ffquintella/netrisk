using System;

namespace Model.Exceptions;

public class DatabaseException: Exception
{
    public string DatabaseExceptionMessage { get; set; }
    
    public DatabaseException(string exceptionMessage)
    {
        DatabaseExceptionMessage = exceptionMessage;
    }
}