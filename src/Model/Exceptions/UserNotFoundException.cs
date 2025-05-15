using System;

namespace Model.Exceptions;

public class UserNotFoundException(string s) : Exception(s)
{
    
}