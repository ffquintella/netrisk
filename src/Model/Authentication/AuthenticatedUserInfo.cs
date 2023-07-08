using System.Collections.Generic;

namespace Model.Authentication;

public class AuthenticatedUserInfo
{
    public string? UserName
    {
        get;
        set;
    }
    
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
    
    public List<string>? UserPermissions
    {
        get;
        set;
    }
}