using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SyncContracts;
using Tools.Security;
using WebSiteData.Entities;

namespace WebSiteData.Services;

public class LocalFixReportService : ILocalFixReportService
{
    private readonly IDbContextFactory<WebSiteDbContext> _factory;
    private readonly IOutboxService _outbox;

    public LocalFixReportService(IDbContextFactory<WebSiteDbContext> factory, IOutboxService outbox)
    {
        _factory = factory;
        _outbox = outbox;
    }

    public async Task<LocalFixRequest?> GetByIdentifierAsync(string identifier)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.FixRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Identifier == identifier);
    }

    public async Task<List<LocalComment>> GetCommentsAsync(int fixRequestId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Comments.AsNoTracking()
            .Where(x => x.FixRequestId == fixRequestId)
            .OrderBy(x => x.Date)
            .ToListAsync();
    }

    public async Task<LocalTeam?> GetTeamAsync(int teamId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Teams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == teamId);
    }

    public async Task<List<LocalTeamUser>> GetTeamUsersAsync(int teamId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.TeamUsers.AsNoTracking().Where(x => x.TeamId == teamId).ToListAsync();
    }

    public Task QueueStatusChangeAsync(FixRequestStatusChangeDto dto)
        => _outbox.EnqueueAsync(SyncActionTypes.FixRequestStatusChange, dto);

    public Task QueueCommentAsync(CommentCreateDto dto)
        => _outbox.EnqueueAsync(SyncActionTypes.CommentCreate, dto);

    public Task QueueMessageAsync(MessageSendDto dto)
        => _outbox.EnqueueAsync(SyncActionTypes.MessageSend, dto);
}

public class LocalUserService : ILocalUserService
{
    private readonly IDbContextFactory<WebSiteDbContext> _factory;
    private readonly IOutboxService _outbox;

    public LocalUserService(IDbContextFactory<WebSiteDbContext> factory, IOutboxService outbox)
    {
        _factory = factory;
        _outbox = outbox;
    }

    public async Task<LocalUser?> GetByIdAsync(int userValue)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Value == userValue);
    }

    public bool CheckPasswordComplexity(string password) => PasswordTools.CheckPasswordComplexity(password);

    public Task QueuePasswordChangeAsync(int userValue, string newPassword)
        => _outbox.EnqueueAsync(SyncActionTypes.PasswordChange,
            new PasswordChangeDto { UserId = userValue, NewPassword = newPassword });
}

public class LocalLinkService : ILocalLinkService
{
    private readonly IDbContextFactory<WebSiteDbContext> _factory;
    private readonly IOutboxService _outbox;

    public LocalLinkService(IDbContextFactory<WebSiteDbContext> factory, IOutboxService outbox)
    {
        _factory = factory;
        _outbox = outbox;
    }

    public async Task<string?> GetLinkDataAsync(string type, string key)
    {
        // Track 7 finding NR-2026-014. These rows are pushed here verbatim by the API, so the digest
        // has to be whatever the API stored — which is why the choice lives in the shared
        // Tools.Security.LinkKeyHash rather than being spelled out on each side. Hashing with MD5
        // here while the API stored SHA-256 would make every password-reset link look expired, with
        // nothing logged.
        var primary = LinkKeyHash.Primary(key);
        var legacy = LinkKeyHash.Legacy(key);

        await using var db = await _factory.CreateDbContextAsync();
        var link = await db.Links.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Type == type && (l.KeyHash == primary || l.KeyHash == legacy));
        if (link == null || link.Consumed) return null;
        if (link.ExpirationDate != null && link.ExpirationDate < DateTime.UtcNow) return null;
        return link.DataJson;
    }

    public async Task QueueLinkDeleteAsync(string type, string key)
    {
        await _outbox.EnqueueAsync(SyncActionTypes.LinkDelete, new LinkDeleteDto { Type = type, Key = key });

        // Mark the link consumed locally so it is single-use on the website even before the
        // server applies the delete on the next sync. We keep the row (rather than delete it)
        // so a fast push — which still lists the link until the delete is applied — cannot
        // resurrect it.
        var primary = LinkKeyHash.Primary(key);
        var legacy = LinkKeyHash.Legacy(key);
        await using var db = await _factory.CreateDbContextAsync();
        var link = await db.Links
            .FirstOrDefaultAsync(l => l.Type == type && (l.KeyHash == primary || l.KeyHash == legacy));
        if (link != null)
        {
            link.Consumed = true;
            await db.SaveChangesAsync();
        }
    }
}

public class LocalIrpService : ILocalIrpService
{
    private readonly IDbContextFactory<WebSiteDbContext> _factory;
    private readonly IOutboxService _outbox;

    public LocalIrpService(IDbContextFactory<WebSiteDbContext> factory, IOutboxService outbox)
    {
        _factory = factory;
        _outbox = outbox;
    }

    public async Task<LocalIrpTaskExecution?> GetTaskExecutionByReportKeyAsync(string reportKey)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var link = await FindLiveReportLinkAsync(db, reportKey);
        if (link == null) return null;

        var taskExecutionId = ReadTaskExecutionId(link);
        if (taskExecutionId == null) return null;

        return await db.IrpTaskExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == taskExecutionId.Value);
    }

    public async Task<bool> QueueTaskReportAsync(string reportKey, int newStatus)
    {
        var execution = await GetTaskExecutionByReportKeyAsync(reportKey);
        if (execution == null) return false;

        // Consume first, atomically: of two concurrent reports with the same key only the one that flips
        // Consumed queues anything. The row is kept (not deleted) so a fast push that still lists the
        // link cannot resurrect it — the same rule as password-reset links.
        var keyHash = LinkKeyHash.Primary(reportKey);
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var consumed = await db.Links
                .Where(l => l.Type == IrpTaskReportLink.Type && l.KeyHash == keyHash && !l.Consumed)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Consumed, true));
            if (consumed != 1) return false;
        }

        await _outbox.EnqueueAsync(SyncActionTypes.IrpTaskStatusChange,
            new IrpTaskStatusChangeDto
            {
                TaskExecutionId = execution.Id,
                NewStatus = newStatus,
                ActionTimeUtc = DateTime.UtcNow,
                ReportKey = reportKey
            });
        return true;
    }

    /// <summary>
    /// The live report link for <paramref name="reportKey"/>. Only the key's SHA-256 is ever stored, so
    /// the lookup is on the digest; the matched digest is then re-compared in constant time, so the
    /// answer never depends on a short-circuiting string comparison of secret-derived values.
    /// </summary>
    private static async Task<LocalLink?> FindLiveReportLinkAsync(WebSiteDbContext db, string reportKey)
    {
        if (!IrpTaskReportLink.IsWellFormedKey(reportKey)) return null;

        var keyHash = LinkKeyHash.Primary(reportKey);
        var link = await db.Links.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Type == IrpTaskReportLink.Type && l.KeyHash == keyHash);
        if (link == null || link.Consumed) return null;

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(link.KeyHash), Encoding.ASCII.GetBytes(keyHash)))
            return null;

        if (link.ExpirationDate != null && link.ExpirationDate < DateTime.UtcNow) return null;
        return link;
    }

    private static int? ReadTaskExecutionId(LocalLink link)
    {
        if (string.IsNullOrEmpty(link.DataJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<IrpTaskReportLinkData>(link.DataJson)?.TaskExecutionId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<LocalIrpTask?> GetTaskAsync(int taskId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.IrpTasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == taskId);
    }

    public async Task<LocalIncident?> GetIncidentForTaskAsync(int taskId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var task = await db.IrpTasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == taskId);
        if (task?.IncidentId == null) return null;
        return await db.Incidents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == task.IncidentId);
    }

}
