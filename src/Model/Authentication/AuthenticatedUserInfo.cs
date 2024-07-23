using System.Collections.Generic;
using LiteDB;

namespace Model.Authentication;

public class AuthenticatedUserInfo
{
    public string? UserName
    {
        get;
        set;
    }
    
    [BsonId]
    public int? UserId
    {
        get;
        set;
    }
    public string? UserAccount  
    {
        get;
        set;
    }
    
    public string? UserEmail
    {
        get;
        set;
    }
    
    public string? UserRole
    {
        get;
        set;
    }
    
    public bool IsAdmin
    {
        get;
        set;
    }
    
    public List<string>? UserPermissions
    {
        get;
        set;
    }
}