using System;

namespace Model.Exceptions;

public class InvalidParameterException: Exception
{
    public string ParameterName { get; set; }
    public InvalidParameterException(string parameterName, string message) : base(message)
    {
        ParameterName = parameterName;
    }
}