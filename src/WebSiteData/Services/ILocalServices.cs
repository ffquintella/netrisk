using WebSiteData.Entities;

namespace WebSiteData.Services;

public interface IOutboxService
{
    Task EnqueueAsync(string actionType, object payload, DateTime? actionTimeUtc = null);
    Task<List<OutboxItem>> GetPendingAsync();
    Task MarkSentAsync(IEnumerable<Guid> ids);
    Task MarkAckedAsync(IEnumerable<Guid> ids);
}

public interface ILocalFixReportService
{
    Task<LocalFixRequest?> GetByIdentifierAsync(string identifier);
    Task<List<LocalComment>> GetCommentsAsync(int fixRequestId);
    Task<LocalTeam?> GetTeamAsync(int teamId);
    Task<List<LocalTeamUser>> GetTeamUsersAsync(int teamId);

    Task QueueStatusChangeAsync(SyncContracts.FixRequestStatusChangeDto dto);
    Task QueueCommentAsync(SyncContracts.CommentCreateDto dto);
    Task QueueMessageAsync(SyncContracts.MessageSendDto dto);
}

public interface ILocalUserService
{
    Task<LocalUser?> GetByIdAsync(int userValue);
    bool CheckPasswordComplexity(string password);
    Task QueuePasswordChangeAsync(int userValue, string newPassword);
}

public interface ILocalLinkService
{
    /// <summary>Returns the link's JSON payload (mirrors the server's byte[] Data), or null if absent/expired.</summary>
    Task<string?> GetLinkDataAsync(string type, string key);
    Task QueueLinkDeleteAsync(string type, string key);
}

/// <summary>
/// The incident-response task report page's data (NR-2026-037). There is deliberately no lookup or
/// write by task-execution id: the only way in is the e-mailed report key, so the page can reach
/// exactly the one execution the key was issued for.
/// </summary>
public interface ILocalIrpService
{
    /// <summary>
    /// The execution a live, unconsumed <c>irpTaskReport</c> link binds, or null for any key that is
    /// malformed, unknown, of another link type, expired or already used — the caller cannot tell which.
    /// </summary>
    Task<LocalIrpTaskExecution?> GetTaskExecutionByReportKeyAsync(string reportKey);

    Task<LocalIrpTask?> GetTaskAsync(int taskId);
    Task<LocalIncident?> GetIncidentForTaskAsync(int taskId);

    /// <summary>
    /// Re-resolves <paramref name="reportKey"/>, consumes its link and queues the status change for the
    /// execution the link binds, carrying the key so the API can re-check the binding. False, with
    /// nothing queued, when the key does not resolve (see <see cref="GetTaskExecutionByReportKeyAsync"/>)
    /// or was consumed by a concurrent report.
    /// </summary>
    Task<bool> QueueTaskReportAsync(string reportKey, int newStatus);
}
