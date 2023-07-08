using System.Security.Principal;

namespace API.Tools;

public static class UserHelper
{
    public static string? GetUserName(IIdentity? userIdentity)
    {
        if (userIdentity == null) return null;
        if (userIdentity.Name == null) return null;
        
        var userName = userIdentity.Name;

        if (userName.Contains('@')) userName = userName.Split('@')[0];

        return userName;
    }
}