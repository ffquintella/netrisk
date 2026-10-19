using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace ServerServices.RiskIndex.Profiles;

/// <summary>
/// Who wrote a profile (S39 §13.1; S40 §6.4, §11.4): its author, every editor and its submitter, from the profile's own
/// columns and <c>risk_index_profile_editors</c> — written in the same save as each edit, never read from
/// <c>audit_logs</c>, which the audit retention purges. The approval's <c>sod_author_cannot_approve</c> and the shadow rule
/// of the profile's values both read this one set.
/// </summary>
internal static class RiskIndexProfileAuthors
{
    public static async Task<IReadOnlySet<int>> ReadAsync(NRDbContext db, RiskIndexProfile profile, CancellationToken ct)
    {
        var authors = (await db.RiskIndexProfileEditors.AsNoTracking()
                .Where(e => e.ProfileId == profile.Id && e.UserId != null)
                .Select(e => e.UserId!.Value).ToListAsync(ct))
            .ToHashSet();
        if (profile.CreatedById is { } created) authors.Add(created);
        if (profile.SubmittedById is { } submitted) authors.Add(submitted);
        return authors;
    }

    /// <summary>Adds or touches the editor row, tracked so the caller's own save writes it with the edit.</summary>
    public static async Task TrackAsync(NRDbContext db, RiskIndexProfile profile, int userId, DateTime now, CancellationToken ct)
    {
        if (userId <= 0) return;

        var row = db.ChangeTracker.Entries<RiskIndexProfileEditor>()
                      .Select(e => e.Entity).FirstOrDefault(e => ReferenceEquals(e.Profile, profile) && e.UserId == userId)
                  ?? (profile.Id > 0
                      ? await db.RiskIndexProfileEditors.FirstOrDefaultAsync(e => e.ProfileId == profile.Id && e.UserId == userId, ct)
                      : null);

        if (row is null)
            db.RiskIndexProfileEditors.Add(new RiskIndexProfileEditor { Profile = profile, UserId = userId, CreatedAt = now });
        else
            row.UpdatedAt = now;
    }
}
