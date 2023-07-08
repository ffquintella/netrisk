using System;

namespace Model.Exceptions;

public class RestComunicationException: Exception
{
    
    public string RestExceptionMessage { get; set; }
    
    public RestComunicationException(string restException, Exception? innerException = null) : base(restException, innerException)
    {
        RestExceptionMessage = restException;
    }   

}