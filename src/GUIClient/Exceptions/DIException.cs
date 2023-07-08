using System;

namespace GUIClient.Exceptions;

public class DIException: Exception
{
    public DIException(string message) : base(message)
    {
    }
}