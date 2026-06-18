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

public interface ILocalIrpService
{
    Task<LocalIrpTaskExecution?> GetTaskExecutionAsync(int id);
    Task<LocalIrpTask?> GetTaskAsync(int taskId);
    Task<LocalIncident?> GetIncidentForTaskAsync(int taskId);
    Task QueueTaskStatusChangeAsync(int taskExecutionId, int newStatus);
}
