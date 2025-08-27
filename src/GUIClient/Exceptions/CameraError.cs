using System;

namespace GUIClient.Exceptions;

public class CameraError: Exception 
{
    public CameraError(string message) : base(message)
    {
    }
}