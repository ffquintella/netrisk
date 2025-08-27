using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using ServerServices.Interfaces;

namespace API.Security;

internal class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    const string POLICY_PREFIX = "Permission";
    private IConfiguration Configuration { get; }
    private IFaceIDService FaceIdService { get; }
    private IPluginsService PluginsService { get; }
    
    public PermissionPolicyProvider(IConfiguration configuration, IFaceIDService faceIdService, IPluginsService pluginsService)
    {
        Configuration = configuration;
        FallbackPolicyProvider = new DefaultPolicyProvider(Configuration);
        FaceIdService = faceIdService;
        PluginsService = pluginsService;
    }

    private IAuthorizationPolicyProvider FallbackPolicyProvider { get; } 

    // Policies are looked up by string name, so expect 'parameters' (like age)
    // to be embedded in the policy names. This is abstracted away from developers
    // by the more strongly-typed attributes derived from AuthorizeAttribute
    // (like [MinimumAgeAuthorize()] in this sample)
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {

        var policy = new AuthorizationPolicyBuilder();
        if(Configuration["Saml2:Enabled"] == "True")
            policy.AddAuthenticationSchemes("headerSelector", "BasicAuthentication", "Bearer", "saml2.cookies", "saml2");
        else policy.AddAuthenticationSchemes("headerSelector", "BasicAuthentication", "Bearer");
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new ValidUserRequirement());
        
        if (policyName.StartsWith(POLICY_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            var permission = policyName.Substring(POLICY_PREFIX.Length);
            policy.AddRequirements(new PermissionRequirement(permission));
            return Task.FromResult(policy.Build())!;
        }
       
       // Legacy policies.

        switch (policyName)
        {
            case "RequireValidUser":
            {
               return Task.FromResult(policy.Build())!;
            }
            case "RequireGovernanceAccess":
            {
               policy.RequireClaim("Permission", new[] {"governance"});
               return Task.FromResult(policy.Build())!;
            }
            case "RequireAssessmentAccess":
            {
               policy.RequireClaim("Permission", new[] {"assessments"});
               return Task.FromResult(policy.Build())!;
            }
            case "RequireRiskmanagement":
            {
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && c.Value == "Administrator") ||
                       (c.Type == "Permission" && c.Value == "riskmanagement")));
               return Task.FromResult(policy.Build())!;
            }
            case "RequireSubmitRisk":
            {
               policy.Requirements.Add(new ClaimsAuthorizationRequirement("Permission", new []{"submit_risks"}));
               return Task.FromResult(policy.Build())!;
            }
            case "RequireDeleteRisk":
            {
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && c.Value=="Admin") || 
                       (c.Type == "Permission" && c.Value == "delete_risk")));
               return Task.FromResult(policy.Build())!;
            }
            case "RequireCloseRisk":
            {
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && c.Value=="Admin") || 
                       (c.Type == "Permission" && c.Value == "close_risks")));
               return Task.FromResult(policy.Build())!;
            }  
            case "RequireMgmtReviewAccess":
            {
               policy.Requirements.Add(new ClaimsAuthorizationRequirement("Permission", new []
               {
                   "review_insignificant", "review_low", "review_medium", "review_high", "review_veryhigh"
               }));
               return Task.FromResult(policy.Build())!;
            }  
            case "RequirePlanMitigations":
            {
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && c.Value=="Administrator") || 
                       (c.Type == "Permission" && c.Value == "plan_mitigations")));
               return Task.FromResult(policy.Build())!;
            }  
            case "RequireAcceptMitigation":
            {
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && c.Value=="Administrator") || 
                       (c.Type == "Permission" && c.Value == "accept_mitigation")));
               return Task.FromResult(policy.Build())!;
            }  
            case "RequireMitigation":
            {
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && c.Value=="Administrator") ||
                       (c.Type == "Permission" && c.Value == "accept_mitigation") || 
                       (c.Type == "Permission" && c.Value == "plan_mitigations")));
               return Task.FromResult(policy.Build())!;
            }
            case "RequireValidFaceIdTransaction":
            {
                policy.RequireAssertion(context =>
                {
                if (FaceIdService.IsFaceIDPluginEnabled().GetAwaiter().GetResult() == false)
                   return true;

                var claims = context.User.Claims;
                if (!claims.Any(c => c.Type == ClaimTypes.Sid))
                    return false;
                
                var userId = Int32.Parse(context.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Sid)!.Value);

                var userIsFaceIdEnabled = FaceIdService.IsUserEnabledAsync(userId).GetAwaiter().GetResult();

                if(!userIsFaceIdEnabled) return false;

                var userHasFaceSet = FaceIdService.UserHasFaceSetAsync(userId).GetAwaiter().GetResult();

                if (!userHasFaceSet) return false;

                // Check if the user has a valid FaceId transaction
                if (!context.User.HasClaim(c => c.Type == "FaceIdTransaction"))
                  return false;
                
                var faceIdTransaction = context.User.Claims.FirstOrDefault(c => c.Type == "FaceIdTransaction")!.Value;
                
                var userTransactionList = FaceIdService.GetUserOpenTransactionsAsync(userId).GetAwaiter().GetResult();
                
                if (userTransactionList == null || userTransactionList.Count == 0)
                    return false;

                var userTransaction =
                    userTransactionList.FirstOrDefault(ut => ut.TransactionId.ToString() == faceIdTransaction);
                
                if(userTransaction == null)
                    return false;
                

                return true;
                });
                return Task.FromResult(policy.Build())!;
            }
            case "RequireAdminOnly":
            {
               
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && c.Value=="Administrator") ||
                       (c.Type == ClaimTypes.Role && c.Value=="Admin") ));
               
               return Task.FromResult(policy.Build())!;
            }

       } 
        
        return Task.FromResult<AuthorizationPolicy?>(null);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => FallbackPolicyProvider.GetDefaultPolicyAsync();
        
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => FallbackPolicyProvider.GetFallbackPolicyAsync();
    

}

