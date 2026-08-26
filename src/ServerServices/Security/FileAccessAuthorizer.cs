using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Security;

/// <summary>
/// Per-file access control for attachments (security finding NR-2026-017).
///
/// Track 7 left this open with a stated reason: an attachment is reachable through six different
/// parents — a risk, a mitigation, an incident, an incident response plan, an IRP execution or task,
/// and a risk acceptance — each with its own permission rules, and inventing that model inside a
/// hardening pass would have been a guess at product behaviour. The model is now this:
///
/// <list type="number">
/// <item>The <c>entity_id</c> column and its query filter close cross-tenant reads outright: a file
/// belonging to another business entity is not found, whoever asks.</item>
/// <item>Within an entity, the caller must be able to reach the file's <em>parent</em>. The parent's
/// own permission is the authority, because that is what the product already means by "may see this
/// risk" or "may see this incident".</item>
/// <item>The uploader may always read back what they uploaded, which is what makes an upload-then-
/// attach flow work, and an administrator may read anything.</item>
/// <item>A file with no parent at all is readable only by its uploader. Those are uploads mid-flow;
/// before this, the unguessable name was the only thing protecting them.</item>
/// </list>
///
/// The unguessable unique name stays — defence in depth — but it is no longer the whole control.
/// </summary>
public class FileAccessAuthorizer(ILogger logger, IDalService dalService, IPermissionsService permissions)
    : ServiceBase(logger, dalService), IFileAccessAuthorizer
{
    /// <summary>The permission that grants access to the risk register and everything hanging off it.</summary>
    public const string RiskPermission = "riskmanagement";

    public const string IncidentPermission = "incident_management";

    public const string IncidentResponsePlanPermission = "incident-response-plans";

    /// <summary>Finding acceptances are part of the vulnerability register, as in Track 3.</summary>
    public const string AcceptancePermission = "vulnerabilities";

    public async Task EnsureCanReadAsync(NrFile file, User user)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(user);

        if (user.Admin) return;

        // The uploader always can. Anything else would break the upload-then-attach flow, where the
        // file exists with no parent for the length of one dialog.
        if (file.User == user.Value) return;

        await using var db = DalService.GetContext();

        var required = await RequiredPermissionAsync(db, file);

        if (required is null)
        {
            Logger.Warning(
                "Refused file {File} to user {User}: the file has no parent record, so only its " +
                "uploader can read it", file.Id, user.Value);

            throw new UserNotAuthorizedException(user.Name, user.Value, "files");
        }

        var held = await permissions.GetUserPermissionsAsync(user);
        if (held.Any(p => string.Equals(p, required, StringComparison.OrdinalIgnoreCase))) return;

        // The register's own relationship rules are the fallback: a risk's owner, manager or
        // submitter can read the risk without the blanket permission, so they can read its
        // attachments too.
        if (required == RiskPermission && await IsRelatedToRiskAsync(db, file, user.Value)) return;

        Logger.Warning("Refused file {File} to user {User}: {Permission} required", file.Id, user.Value,
            required);

        throw new UserNotAuthorizedException(user.Name, user.Value, required);
    }

    /// <summary>
    /// The permission the file's parent is gated on, or null when it has no parent.
    ///
    /// Checked in the order the attachment columns were added, which is also least-to-most specific:
    /// a file carries at most one of these, but a row that somehow carries two is resolved
    /// deterministically rather than by whichever branch happens to run first.
    /// </summary>
    private static async Task<string?> RequiredPermissionAsync(AuditableContext db, NrFile file)
    {
        if (file.RiskId is not null) return RiskPermission;

        if (file.MitigationId is not null)
        {
            // A mitigation is a child of a risk, so the risk's permission governs. Confirming the
            // mitigation exists keeps a dangling FK from silently granting access.
            var exists = await db.Mitigations.AnyAsync(m => m.Id == file.MitigationId.Value);
            return exists ? RiskPermission : null;
        }

        if (file.RiskAcceptanceId is not null) return AcceptancePermission;

        if (file.IncidentId is not null) return IncidentPermission;

        if (file.IncidentResponsePlanId is not null ||
            file.IncidentResponsePlanExecutionId is not null ||
            file.IncidentResponsePlanTaskId is not null ||
            file.IncidentResponsePlanTaskExecutionId is not null)
            return IncidentResponsePlanPermission;

        return null;
    }

    /// <summary>
    /// Whether the caller is the owner, manager or submitter of the risk the file hangs off —
    /// directly or through a mitigation.
    /// </summary>
    private static async Task<bool> IsRelatedToRiskAsync(AuditableContext db, NrFile file, int userId)
    {
        var riskId = file.RiskId;

        if (riskId is null && file.MitigationId is not null)
            riskId = await db.Mitigations.Where(m => m.Id == file.MitigationId.Value)
                .Select(m => (int?)m.RiskId).FirstOrDefaultAsync();

        if (riskId is null) return false;

        return await db.Risks.AnyAsync(r => r.Id == riskId.Value &&
                                            (r.Owner == userId || r.Manager == userId ||
                                             r.SubmittedBy == userId));
    }
}
