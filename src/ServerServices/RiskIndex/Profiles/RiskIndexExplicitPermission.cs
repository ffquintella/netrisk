using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.Interfaces;
using ServerServices.RiskIndex.Read;
using ServerServices.Services;

namespace ServerServices.RiskIndex.Profiles;

/// <summary>
/// The committee's actions — approving, suspending, publishing, attaching the minutes — need the permission itself, read
/// from the database (role ∪ direct grants, <c>PermissionsService.GetUserPermissionsAsync</c>), never the token's claims nor
/// the administrator flag, which passes every policy attribute (S40 §6.4, §11.1, §11.4).
/// </summary>
internal static class RiskIndexExplicitPermission
{
    public static async Task<IReadOnlyList<string>> ReadAsync(IDalService dalService, IPermissionsService permissions, int userId,
        CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Value == userId, ct);
        return user is null ? [] : await permissions.GetUserPermissionsAsync(user);
    }

    public static async Task RequireAsync(IDalService dalService, IPermissionsService permissions, int userId, string permission,
        CancellationToken ct)
    {
        if ((await ReadAsync(dalService, permissions, userId, ct)).Contains(permission)) return;
        throw RiskIndexException.Forbidden(RiskIndexErrors.ApproverPermissionNotExplicit,
            $"{permission} must be granted to you, not only implied by the administrator flag.");
    }
}

/// <summary>
/// Who reads what a profile produced beyond the global-scope rule (S39 §13.3; S40 §8.4, §11.3): during the shadow pilot,
/// administrators, the committee (explicit <c>risk_index_approve</c>) and, with <c>risk_index_configure</c>, the profile's
/// own authors, editors and submitter. Shared by the preview, bridge and history reads and by the committee's minutes.
/// </summary>
internal static class RiskIndexShadowRule
{
    public static async Task<RiskIndexException?> RefusalAsync(DAL.Context.NRDbContext db, DAL.Entities.RiskIndexProfile profile,
        int userId, bool isAdmin, Func<string, bool> holds, CancellationToken ct)
    {
        if (isAdmin || holds(RiskIndexPermissions.Approve)) return null;

        var published = string.Equals((await db.Settings.AsNoTracking()
                .Where(s => s.Name == DAL.Enums.RiskIndexSettingKeys.Publication).Select(s => s.Value).FirstOrDefaultAsync(ct))?.Trim(),
            DAL.Enums.RiskIndexSettingKeys.PublicationPublished, StringComparison.OrdinalIgnoreCase);
        if (published) return null;

        if (holds(RiskIndexPermissions.Configure) && (await RiskIndexProfileAuthors.ReadAsync(db, profile, ct)).Contains(userId))
            return null;

        return RiskIndexException.Forbidden(RiskIndexErrors.InShadow,
            "The index is in its shadow pilot: the committee, administrators and this draft's authors read its values.");
    }
}
