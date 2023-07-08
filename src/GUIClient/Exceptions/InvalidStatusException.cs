using System;

namespace GUIClient.Exceptions;

public class InvalidStatusException: Exception
{
    public String Status { get; set; }
    public InvalidStatusException(string message, string status) : base(message)
    {
        Status = status;
    }
}