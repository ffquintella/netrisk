using Microsoft.AspNetCore.Authorization;

namespace API.Security;

public class UserInRoleRequirement: IAuthorizationRequirement
{
    public string Role { get; }

    public UserInRoleRequirement(string role)
    {
        Role = role;
    }
}