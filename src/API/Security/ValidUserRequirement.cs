using Microsoft.AspNetCore.Authorization;

namespace API.Security;

public class ValidUserRequirement : IAuthorizationRequirement
{
    public UserType UserType { get;}
    public ValidUserRequirement(UserType userType = UserType.Any)
    {
        UserType = userType;
    }
}