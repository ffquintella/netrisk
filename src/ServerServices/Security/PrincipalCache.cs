using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Model.Exceptions;
using ServerServices.Services;

namespace ServerServices.Security;

/// <summary>
/// What an authentication handler resolves for a user beyond the user row itself: the role's name, the permission keys
/// (see <see cref="MergePermissions"/>) and the entities the user holds an unrevoked assignment in. The user row is not
/// part of it: the handlers read that live on every request, so a disabled, locked, deleted or re-roled user is never
/// answered from here.
/// </summary>
/// <param name="RoleName">Null when <paramref name="RoleId"/> is 0 (no role).</param>
public sealed record PrincipalSnapshot(
    int UserId,
    int RoleId,
    string? RoleName,
    IReadOnlyList<string> PermissionKeys,
    IReadOnlyList<int> EntityIds)
{
    /// <summary>
    /// The rule <c>PermissionsService.GetUserPermissionsAsync</c> has always applied: every permission of the role, in
    /// order, then each of the user's own permissions the list does not hold yet.
    /// </summary>
    public static List<string> MergePermissions(IEnumerable<string> rolePermissions, IEnumerable<string> userPermissions)
    {
        var permissions = new List<string>(rolePermissions);
        var seen = new HashSet<string>(permissions);
        foreach (var permission in userPermissions)
            if (seen.Add(permission)) permissions.Add(permission);
        return permissions;
    }
}

/// <summary>
/// The process-wide cache of <see cref="PrincipalSnapshot"/>s the authentication handlers build their claims from. Every
/// save that changes an input drops all of it (<c>AuditableContext.PrincipalInputsChanged</c>, wired by
/// <see cref="DalService"/>), so a change made through this process applies on the next request.
/// </summary>
public interface IPrincipalCache
{
    /// <summary>How long an entry is trusted without an invalidation. <see cref="TimeSpan.Zero"/>: the cache is off.</summary>
    TimeSpan TimeToLive { get; }

    /// <summary>Increments on every invalidation. A loader reads it before loading and hands it to <see cref="Store"/>.</summary>
    long Generation { get; }

    /// <summary>
    /// The snapshot of <paramref name="userId"/>, when one is younger than the time to live, was loaded after the last
    /// invalidation, and was built for <paramref name="roleId"/> — the role the live user row holds.
    /// </summary>
    bool TryGet(int userId, int roleId, out PrincipalSnapshot? snapshot);

    /// <summary>
    /// Keeps <paramref name="snapshot"/>, unless the cache is off or an invalidation happened since
    /// <paramref name="generationAtLoad"/> was read: the rows may predate that change, and keeping them would undo it.
    /// </summary>
    void Store(PrincipalSnapshot snapshot, long generationAtLoad);

    /// <summary>Drops every snapshot. Synchronous: when it returns, nothing cached before it is served.</summary>
    void Invalidate();
}

/// <summary>
/// <see cref="IPrincipalCache"/> over a <see cref="MemoryCache"/> of its own: the host's shared one has a size limit that
/// a burst of principals would fill, starving the sign-in flows' entries. Invalidation is local to the process; a change
/// made elsewhere (another API instance, the console, hand-written SQL) is seen when the entry's time to live runs out.
/// The user row is never cached, so disabling, locking, deleting, a password change or a role move apply at once.
/// </summary>
public sealed class PrincipalCache : IPrincipalCache, IDisposable
{
    /// <summary>The configuration key: whole seconds, 0 turns the cache off. Not a secret.</summary>
    public const string ConfigurationKey = "Security:PrincipalCacheSeconds";

    /// <summary>Short, because it bounds the cross-process staleness of an authorization decision.</summary>
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(30);

    /// <summary>A configured value above this is lowered to it: a longer window is a policy decision, not a tuning knob.</summary>
    public static readonly TimeSpan MaximumTimeToLive = TimeSpan.FromMinutes(5);

    /// <summary>Entries, one per user: a full cache stops storing, which only costs the next request a load.</summary>
    public const long DefaultSizeLimit = 10_000;

    private sealed record Entry(PrincipalSnapshot Snapshot, long Generation, DateTime LoadedAt);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = DefaultSizeLimit });
    private readonly Func<DateTime> _clock;
    private long _generation;

    public PrincipalCache(TimeSpan timeToLive, Func<DateTime>? clock = null)
    {
        TimeToLive = timeToLive <= TimeSpan.Zero ? TimeSpan.Zero
            : timeToLive > MaximumTimeToLive ? MaximumTimeToLive
            : timeToLive;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>Reads <see cref="ConfigurationKey"/> (see <see cref="TimeToLiveFrom"/>).</summary>
    public static PrincipalCache FromConfiguration(IConfiguration configuration) =>
        new(TimeToLiveFrom(configuration[ConfigurationKey]));

    /// <summary>
    /// Absent gives <see cref="DefaultTimeToLive"/>; a value that is not a whole number of seconds, or is negative, turns
    /// the cache off rather than guessing.
    /// </summary>
    public static TimeSpan TimeToLiveFrom(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return DefaultTimeToLive;

        if (!int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
        {
            Serilog.Log.Warning("{Key} is not a whole number of seconds; the principal cache is off", ConfigurationKey);
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public TimeSpan TimeToLive { get; }

    public long Generation => Interlocked.Read(ref _generation);

    private static string Key(int userId) => "principal:" + userId.ToString(CultureInfo.InvariantCulture);

    public bool TryGet(int userId, int roleId, out PrincipalSnapshot? snapshot)
    {
        snapshot = null;
        if (TimeToLive == TimeSpan.Zero) return false;

        if (!_cache.TryGetValue(Key(userId), out Entry? entry) || entry is null) return false;

        if (entry.Generation != Generation || entry.Snapshot.UserId != userId || entry.Snapshot.RoleId != roleId ||
            _clock() - entry.LoadedAt >= TimeToLive)
            return false;

        snapshot = entry.Snapshot;
        return true;
    }

    public void Store(PrincipalSnapshot snapshot, long generationAtLoad)
    {
        if (TimeToLive == TimeSpan.Zero || generationAtLoad != Generation) return;

        _cache.Set(Key(snapshot.UserId), new Entry(snapshot, generationAtLoad, _clock()),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeToLive, Size = 1 });
    }

    public void Invalidate()
    {
        // The generation first: from here on no entry stored before it is served, whatever Compact gets to.
        Interlocked.Increment(ref _generation);
        _cache.Compact(1.0);
    }

    public void Dispose() => _cache.Dispose();
}

/// <summary>Builds a user's <see cref="PrincipalSnapshot"/> for the authentication handlers, through the cache.</summary>
public interface IPrincipalResolver
{
    /// <summary>
    /// The snapshot of <paramref name="user"/>, a row the caller has just read and checked. Throws, and caches nothing,
    /// when the user's role or the user no longer exists.
    /// </summary>
    Task<PrincipalSnapshot> ResolveAsync(User user, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IPrincipalResolver"/> over the database. On a miss, three untracked queries on one context — the role with
/// its permissions, the user's own permissions, the active assignments — where the handlers used to make six or seven
/// round trips, each on a context of its own. Each query has the shape of the one it replaces, so the database returns
/// the permissions in the same order and the claims come out exactly as before.
/// </summary>
public sealed class PrincipalResolver(IDalService dalService, IPrincipalCache cache) : IPrincipalResolver
{
    public async Task<PrincipalSnapshot> ResolveAsync(User user, CancellationToken cancellationToken = default)
    {
        if (cache.TryGet(user.Value, user.RoleId, out var cached)) return cached!;

        var generation = cache.Generation;
        var loaded = await LoadAsync(user, cancellationToken);
        cache.Store(loaded, generation);
        return loaded;
    }

    /// <summary>Always from the database.</summary>
    public async Task<PrincipalSnapshot> LoadAsync(User user, CancellationToken cancellationToken = default)
    {
        // Unscoped and without an identity: this runs before any principal exists, as the handlers' reads always did.
        await using var db = dalService.GetContext(withIdentity: false, bypassEntityScope: true);

        string? roleName = null;
        IReadOnlyList<string> rolePermissions = [];
        if (user.RoleId != 0)
        {
            // RolesService.GetRoleAsync and GetRolePermissionsAsync both ran this query; once is enough.
            var role = await db.Roles.AsNoTracking()
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.Value == user.RoleId, cancellationToken);

            if (role == null)
                throw new DataNotFoundException("netrisk", "role",
                    new Exception($"Role with id {user.RoleId} not found"));

            roleName = role.Name;
            // Only a positive role ever contributed permissions; a negative id names no role row.
            if (user.RoleId > 0) rolePermissions = role.Permissions.Select(p => p.Key).ToList();
        }

        // PermissionsService.GetUserPermissionsAsync's query, without the two reads of the user that preceded it.
        var own = await db.Users.AsNoTracking()
            .Include(u => u.Permissions)
            .FirstOrDefaultAsync(u => u.Value == user.Value, cancellationToken);

        if (own == null) throw new UserNotFoundException("The user was not found");

        var entityIds = await db.UserEntityRoles.AsNoTracking()
            .Where(uer => uer.UserId == user.Value && uer.RevokedAt == null)
            .Select(uer => uer.EntityId)
            .ToListAsync(cancellationToken);

        return new PrincipalSnapshot(user.Value, user.RoleId, roleName,
            PrincipalSnapshot.MergePermissions(rolePermissions, own.Permissions.Select(p => p.Key)), entityIds);
    }
}
