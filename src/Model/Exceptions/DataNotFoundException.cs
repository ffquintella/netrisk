using System;

namespace Model.Exceptions;

public class DataNotFoundException: Exception
{
    public String DatabaseName { get; }
    public String Identification { get; }
    
    
    public DataNotFoundException(string databaseName, string identification,
        Exception? innerException = null) : base($"Data of type {databaseName} not found by id {identification}" , innerException)
    {
        DatabaseName = databaseName;
        Identification = identification;
    }  
}