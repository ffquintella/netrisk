namespace Model.Exceptions;

public class DataAlreadyExistsException: Exception
{
    public String DatabaseName { get; }
    
    public String TableName { get; }
    public string Identification { get; }
    
    public DataAlreadyExistsException(string databaseName, string tableName, string identification,
        String message) : base(message)
    {
        TableName = tableName;
        DatabaseName = databaseName;
        Identification = identification;
    } 
}