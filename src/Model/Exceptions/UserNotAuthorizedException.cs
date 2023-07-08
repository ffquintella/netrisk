using System;

namespace Model.Exceptions;

public class UserNotAuthorizedException: Exception
{
    public string UserName { get; set; }
    public int UserId { get; set; }
    
    public string Destination { get; set; }
    
    public UserNotAuthorizedException(string userName, int userId, string destination,
        Exception? innerException = null) : base($"User {userName} is not authorized to {destination}" , innerException)
    {
        UserName = userName;
        UserId = userId;
        Destination = destination;
    }   
}