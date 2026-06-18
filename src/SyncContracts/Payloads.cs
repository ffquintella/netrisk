namespace SyncContracts;

/// <summary>
/// Full snapshot of the read-only data the website renders. The bulk job sends this and the
/// website does a transactional full-replace of its read tables (links excepted — see the
/// fast lane). The website's response to a push carries its queued outbound actions.
/// </summary>
public class PushPayload
{
    public long Cursor { get; set; }
    public List<FixRequestDto> FixRequests { get; set; } = new();
    public List<CommentDto> Comments { get; set; } = new();
    public List<TeamDto> Teams { get; set; } = new();
    public List<UserDto> Users { get; set; } = new();
    public List<IrpTaskExecutionDto> IrpExecutions { get; set; } = new();
    public List<IrpTaskDto> IrpTasks { get; set; } = new();
    public List<IncidentDto> Incidents { get; set; } = new();
}

/// <summary>
/// Security-sensitive, time-critical payload pushed on the frequent "fast" schedule:
/// freshly-created password-reset links (upserted) and links removed on the server (pruned).
/// </summary>
public class FastPushPayload
{
    public List<LinkDto> Links { get; set; } = new();
    public List<int> DeletedLinkIds { get; set; } = new();
}

/// <summary>
/// The website's reply to any signed push: the pending outbox actions it has accumulated
/// since the last successful ack, plus the cursor it last applied.
/// </summary>
public class SyncResponse
{
    public long AppliedCursor { get; set; }
    public List<OutboxActionDto> Actions { get; set; } = new();
}

/// <summary>Server -> website acknowledgement that the listed actions were applied to the
/// main database, so the website can mark them done and stop re-sending.</summary>
public class AckRequest
{
    public List<Guid> AckedActionIds { get; set; } = new();
}

public static class SyncActionTypes
{
    public const string FixRequestStatusChange = "FixRequestStatusChange";
    public const string CommentCreate = "CommentCreate";
    public const string MessageSend = "MessageSend";
    public const string PasswordChange = "PasswordChange";
    public const string LinkDelete = "LinkDelete";
    public const string IrpTaskStatusChange = "IrpTaskStatusChange";
}

/// <summary>One queued action originating from a website visitor, to be applied to the main
/// database. <see cref="ClientActionId"/> is the idempotency key.</summary>
public class OutboxActionDto
{
    public Guid ClientActionId { get; set; }
    public string ActionType { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTime ActionTimeUtc { get; set; }
}

// ---- Per-action payloads (serialized into OutboxActionDto.PayloadJson) ----

public class FixRequestStatusChangeDto
{
    public int FixRequestId { get; set; }
    public int NewStatus { get; set; }
    public string NewStatusLabel { get; set; } = "";
    public DateTime FixDate { get; set; }
    public bool IsTeamFix { get; set; }
    public int? LastReportingUserId { get; set; }
    public string? SingleFixDestination { get; set; }
    public int? RequestingUserId { get; set; }
    public int VulnerabilityId { get; set; }
}

public class CommentCreateDto
{
    public int FixRequestId { get; set; }
    public int? UserId { get; set; }
    public bool IsAnonymous { get; set; }
    public string CommenterName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Date { get; set; }
}

public class MessageSendDto
{
    public string Message { get; set; } = "";
    public int UserId { get; set; }
    public int? ChatId { get; set; }
    public int Type { get; set; }
}

public class PasswordChangeDto
{
    public int UserId { get; set; }
    public string NewPassword { get; set; } = "";
}

public class LinkDeleteDto
{
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
}

public class IrpTaskStatusChangeDto
{
    public int TaskExecutionId { get; set; }
    public int NewStatus { get; set; }
    public DateTime ActionTimeUtc { get; set; }
}
