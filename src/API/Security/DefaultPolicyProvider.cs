using Microsoft.AspNetCore.Authorization;

namespace API.Security;

public class DefaultPolicyProvider: IAuthorizationPolicyProvider
{
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        throw new NotImplementedException();
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
    {
        var policy = new AuthorizationPolicyBuilder();
        policy.AddAuthenticationSchemes("headerSelector", "BasicAuthentication", "Bearer", "saml2.cookies", "saml2");
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new ValidUserRequirement());
        return Task.FromResult(policy.Build())!;
    }

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        return GetDefaultPolicyAsync()!;
    }
}