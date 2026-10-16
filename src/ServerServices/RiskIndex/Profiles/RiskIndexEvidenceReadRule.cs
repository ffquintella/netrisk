using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.Interfaces;
using ServerServices.RiskIndex.Read;
using ServerServices.Services;

namespace ServerServices.RiskIndex.Profiles;

/// <summary>
/// The read rule of a profile's committee minutes (S40 §11.3): <c>risk_index_view</c>, global scope — administrators
/// included — and the shadow rule of the profile's values. <c>FileAccessAuthorizerRiskIndexTest</c> pins each branch.
/// </summary>
public sealed class RiskIndexEvidenceReadRule(IDalService dalService) : IApprovalEvidenceReadRule
{
    public async Task<string?> RefusalAsync(int parentId, User user, IReadOnlyCollection<string> heldPermissions,
        CancellationToken ct = default)
    {
        if (!dalService.GetCurrentEntityScope().IsUnrestricted) return RiskIndexErrors.ProfileValuesRequireGlobalScope;

        bool Holds(string permission) => heldPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
        if (!user.Admin && !Holds(RiskIndexPermissions.View)) return RiskIndexPermissions.View;

        await using var db = dalService.GetContext();
        var profile = await db.RiskIndexProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parentId, ct);
        if (profile is null) return RiskIndexErrors.ProfileNotFound;

        return (await RiskIndexShadowRule.RefusalAsync(db, profile, user.Value, user.Admin, Holds, ct))?.Code;
    }
}
