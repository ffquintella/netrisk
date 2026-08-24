namespace Model.Exceptions;

public class RuleBrokenException: Exception
{
    public string RuleName { get; set; } = string.Empty;
    
    public RuleBrokenException(string ruleName)
    {
        RuleName = ruleName;
    }

    /// <summary>
    /// Carries an explanation alongside the rule name. The single-argument overload leaves
    /// <see cref="Exception.Message"/> as the framework default, which tells a caller nothing
    /// about which rule was broken or why.
    /// </summary>
    public RuleBrokenException(string message, string ruleName) : base(message)
    {
        RuleName = ruleName;
    }
}