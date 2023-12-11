namespace Model.Exceptions;

public class RuleBrokenException: Exception
{
    public string RuleName { get; set; } = string.Empty;
    
    public RuleBrokenException(string ruleName)
    {
        RuleName = ruleName;
    }
}