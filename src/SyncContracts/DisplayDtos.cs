namespace SyncContracts;

/// <summary>
/// Display data for the public fix-report page. Vulnerability/host fields are denormalized
/// so the website never needs the full vulnerability graph.
/// </summary>
public class FixRequestDto
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

public class CommentDto
{
    public int Id { get; set; }
    public int FixRequestId { get; set; }
    public int? UserId { get; set; }
    public bool IsAnonymous { get; set; }
    public string? CommenterName { get; set; }
    public DateTime Date { get; set; }
    public string? Text { get; set; }
}

public class TeamDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public List<TeamUserDto> Users { get; set; } = new();
}

public class TeamUserDto
{
    public int Value { get; set; }
    public string Login { get; set; } = "";
}

/// <summary>User display data for password reset. Never carries the password hash.</summary>
public class UserDto
{
    public int Value { get; set; }
    public string Login { get; set; } = "";
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool? Enabled { get; set; }
}

public class IrpTaskExecutionDto
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class IrpTaskDto
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

public class IncidentDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}

/// <summary>A password-reset (or other) link. <see cref="DataJson"/> is the UTF-8 JSON that
/// is stored as a byte[] on the server side.</summary>
public class LinkDto
{
    public int Id { get; set; }
    public string KeyHash { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime? ExpirationDate { get; set; }
    public string? DataJson { get; set; }
}
