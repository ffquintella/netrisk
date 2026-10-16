using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.DTO;
using Model.RiskIndex;
using ServerServices.RiskIndex.Read;
using ServerServices.Security;

namespace ServerServices.RiskIndex.Profiles;

public sealed partial class RiskIndexProfilesService
{
    /// <summary>
    /// Links the committee's minutes to a profile awaiting approval (S40 §6.4, §8.4; T262). The file was uploaded through
    /// <c>/Files</c> — its size, type and storage are that route's — by the caller, and hangs off nothing else: relinking
    /// somebody's file, or a risk's or incident's attachment, would be a way to read it under the profile's rule.
    /// </summary>
    public async Task AttachAsync(int id, RiskIndexAttachRequest request, RiskIndexCaller caller, CancellationToken ct = default)
    {
        Require(caller, RiskIndexPermissions.Approve);
        await RiskIndexExplicitPermission.RequireAsync(dalService, permissionsService, caller.UserId, RiskIndexPermissions.Approve, ct);
        ArgumentNullException.ThrowIfNull(request);

        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: false, ct);
        if (profile.Status != RiskIndexProfileStatus.PendingApproval)
            throw RiskIndexException.Conflict(RiskIndexErrors.InvalidTransition, "Evidence is attached to a profile awaiting approval.");

        // Read outside the caller's scope: whether the file exists is not the scope's question, and a scoped approver must not
        // tell "not yours" from "not visible" any differently than anyone else.
        await using var unscoped = dalService.GetContext(withIdentity: true, bypassEntityScope: true);
        var file = await unscoped.NrFiles.FirstOrDefaultAsync(f => f.Id == request.FileId, ct)
                   ?? throw RiskIndexException.NotFound(RiskIndexErrors.FileNotFound, "The file does not exist.");
        if (file.User != caller.UserId)
            throw RiskIndexException.Forbidden(RiskIndexErrors.FileNotOwned, "Only a file you uploaded is attached.");

        // nr_files.risk_id defaults to 0 in the schema, so an upload with no parent is stored with risk 0, not null: no risk.
        if (file.RiskId == 0) file.RiskId = null;

        var parents = FileParents.Declared(file);
        if (parents.Count == 1 && parents[0] is { Kind: FileParents.Kind.RiskIndexProfile } same && same.Id == id) return;
        // An entity stamp is a parent's (FilesService derives it from one): such a file belongs to that entity's records.
        if (parents.Count > 0 || file.EntityId is not null)
            throw RiskIndexException.Conflict(RiskIndexErrors.FileAlreadyAttached, "The file is already attached elsewhere.");

        file.RiskIndexProfileId = id;
        await unscoped.SaveChangesAsync(ct);
        logger.Information("Committee evidence {File} attached to risk index profile v{Version} by user {UserId}", file.Id,
            profile.Version, caller.UserId);
    }

    /// <summary>The profile's evidence, listed to any <c>risk_index_view</c> holder; reading a file is the files route's rule.</summary>
    public async Task<IReadOnlyList<FileListing>> ListAttachmentsAsync(int id, CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        await FindAsync(db, id, tracked: false, ct);

        var files = await db.NrFiles.AsNoTracking().Where(f => f.RiskIndexProfileId == id).OrderBy(f => f.Id)
            .Select(f => new { f.Name, f.UniqueName, f.Type, f.Timestamp, f.User }).ToListAsync(ct);
        var types = (await db.FileTypes.AsNoTracking().ToListAsync(ct))
            .GroupBy(t => t.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.First().Name);
        return files.Select(f => new FileListing
        {
            Name = f.Name, UniqueName = f.UniqueName, Type = f.Type is { } t ? types.GetValueOrDefault(t, t) : null,
            Timestamp = f.Timestamp, OwnerId = f.User
        }).ToList();
    }
}
