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

        // Stage 9.9 (S50 §4.7): on every policy this provider builds — permission, legacy and new alike — so no
        // endpoint can be written without it. The third line is refused any write; nobody else is affected.
        policy.Requirements.Add(ThirdLineReadOnlyRequirement.Instance);
        
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
            case "RequireContinuityRead":
            {
               // Stage 9.3 (S43 §6, D11): the exact union of the RequireRiskmanagement audience (the
               // Administrator role or riskmanagement) and the two continuity write audiences (the Admin
               // role or bia_manage / restoration_test_record), so whoever sees the risk register sees
               // the flag 4 basis, and whoever writes continuity data reads what they wrote.
               // ContinuityAuthorizationTest evaluates this policy, accepted and denied.
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && (c.Value == "Admin" || c.Value == "Administrator")) ||
                       (c.Type == "Permission" && (c.Value == "riskmanagement" || c.Value == "bia_manage"
                                                   || c.Value == "restoration_test_record"))));
               return Task.FromResult(policy.Build())!;
            }
            case "RequireAssuranceEvidence":
            {
               // Stage 9.9 (S50 §4.7, §6): the governance evidence pack — administrators, as before, and the third line,
               // whose assurance is exactly this export. RequireAdminOnly does not admit it: the auditor is not an
               // administrator, and an auditor granted the Admin role would be refused every write anyway.
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && (c.Value == "Admin" || c.Value == "Administrator")) ||
                       (c.Type == "Permission" && c.Value == Model.DecisionCycle.ThirdLineAssurance.PermissionKey)));
               return Task.FromResult(policy.Build())!;
            }
            case "RequireThirdPartyRead":
            {
               // Stage 9.10 (S51 §6): the third-party register is read by the risk register's audience (the
               // Administrator role or riskmanagement — the third line included, through its seeded riskmanagement)
               // and by whoever manages it (the Admin role or third_party_manage), so a vendor manager reads what they
               // write. ThirdPartiesAuthorizationTest evaluates this policy, accepted and denied.
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && (c.Value == "Admin" || c.Value == "Administrator")) ||
                       (c.Type == "Permission" && (c.Value == "riskmanagement" || c.Value == "third_party_manage"))));
               return Task.FromResult(policy.Build())!;
            }
            case "RequireDataCatalogueRead":
            {
               // Stage 9.11 (S52 §6, D14): the LGPD data catalogue is read by the risk register's audience (the
               // Administrator role or riskmanagement — the third line included, through its seeded riskmanagement) and by
               // whoever maintains it (the Admin role or data_catalogue_manage), so the DPO's office reads what it writes.
               // DataCatalogueAuthorizationTest evaluates this policy, accepted and denied.
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && (c.Value == "Admin" || c.Value == "Administrator")) ||
                       (c.Type == "Permission" && (c.Value == "riskmanagement" || c.Value == "data_catalogue_manage"))));
               return Task.FromResult(policy.Build())!;
            }
            case "RequireAiGovernanceRead":
            {
               // Stage 9.12 (S53 §6, D10): the AI model inventory is read by the risk register's audience (the
               // Administrator role or riskmanagement — the third line included, through its seeded riskmanagement) and by
               // whoever maintains it (the Admin role or ai_governance_manage), so the AI governance office reads what it
               // writes. AiGovernanceAuthorizationTest evaluates this policy, accepted and denied.
               policy.RequireAssertion(context =>
                   context.User.HasClaim(c =>
                       (c.Type == ClaimTypes.Role && (c.Value == "Admin" || c.Value == "Administrator")) ||
                       (c.Type == "Permission" && (c.Value == "riskmanagement" || c.Value == "ai_governance_manage"))));
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

