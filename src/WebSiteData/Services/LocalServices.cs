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
        var hash = HashTool.CreateMD5(key);
        await using var db = await _factory.CreateDbContextAsync();
        var link = await db.Links.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Type == type && l.KeyHash == hash);
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
        var hash = HashTool.CreateMD5(key);
        await using var db = await _factory.CreateDbContextAsync();
        var link = await db.Links.FirstOrDefaultAsync(l => l.Type == type && l.KeyHash == hash);
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

    public async Task<LocalIrpTaskExecution?> GetTaskExecutionAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.IrpTaskExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
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

    public Task QueueTaskStatusChangeAsync(int taskExecutionId, int newStatus)
        => _outbox.EnqueueAsync(SyncActionTypes.IrpTaskStatusChange,
            new IrpTaskStatusChangeDto
            {
                TaskExecutionId = taskExecutionId,
                NewStatus = newStatus,
                ActionTimeUtc = DateTime.UtcNow
            });
}
