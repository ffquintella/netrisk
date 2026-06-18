namespace WebSiteData.Entities;

/// <summary>
/// Slim local mirrors of the subset of main-DB fields the public website renders. These are
/// populated by the signed sync push and never connect to the main database.
/// </summary>
public class LocalFixRequest
{
    public int Id { get; set; }
    public string Identifier { get; set; } = "";
    public int VulnerabilityId { get; set; }
    public int Status { get; set; }
    public bool IsTeamFix { get; set; }
    public int? FixTeamId { get; set; }
    public string? SingleFixDestination { get; set; }
    public int? RequestingUserId { get; set; }

    public string VulnTitle { get; set; } = "";
    public string? VulnDescription { get; set; }
    public string? VulnSolution { get; set; }
    public double? VulnScore { get; set; }
    public string? HostName { get; set; }
}

public class LocalComment
{
    public int Id { get; set; }
    public int FixRequestId { get; set; }
    public int? UserId { get; set; }
    public bool IsAnonymous { get; set; }
    public string? CommenterName { get; set; }
    public DateTime Date { get; set; }
    public string? Text { get; set; }
}

public class LocalTeam
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public class LocalTeamUser
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int UserValue { get; set; }
    public string Login { get; set; } = "";
}

public class LocalUser
{
    public int Value { get; set; }
    public string Login { get; set; } = "";
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool? Enabled { get; set; }
    // Deliberately no password — the website never verifies passwords.
}

public class LocalLink
{
    public int Id { get; set; }
    public string KeyHash { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime? ExpirationDate { get; set; }
    public string? DataJson { get; set; }

    /// <summary>Set when the link is used on the website. Keeps the row (so a fast push from
    /// the server — which still lists it until the delete is applied — cannot resurrect it)
    /// while making it inert locally.</summary>
    public bool Consumed { get; set; }
}

public class LocalIrpTaskExecution
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LocalIrpTask
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int? IncidentId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public string? ConditionToProceed { get; set; }
    public string? ConditionToSkip { get; set; }
    public string? SuccessCriteria { get; set; }
    public string? FailureCriteria { get; set; }
    public string? CompletionCriteria { get; set; }
    public string? VerificationCriteria { get; set; }
}

public class LocalIncident
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public enum OutboxStatus
{
    Pending = 0,
    Sent = 1,
    Acked = 2,
    Failed = 3
}

/// <summary>A visitor action queued locally to be applied to the main database on the next
/// pull. The outbox is the durability boundary: nothing is lost across sync cycles.</summary>
public class OutboxItem
{
    public Guid ClientActionId { get; set; }
    public string ActionType { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ActionTimeUtc { get; set; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Single-row table holding the enrollment/trust state and the last applied cursor.</summary>
public class SyncState
{
    public int Id { get; set; } = 1;
    public string? ApiKeyId { get; set; }
    public string? ApiPublicKeyPem { get; set; }
    public DateTime? EnrolledAt { get; set; }
    public long BulkCursor { get; set; }
}
