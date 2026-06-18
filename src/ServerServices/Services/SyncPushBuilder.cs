using System.Text;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using Serilog;
using ServerServices.Interfaces;
using SyncContracts;

namespace ServerServices.Services;

public class SyncPushBuilder : ServiceBase, ISyncPushBuilder
{
    private const string PasswordResetLinkType = "passwordReset";

    private static readonly HashSet<int> TerminalFixStatuses = new()
    {
        (int)IntStatus.Fixed, (int)IntStatus.FixNotPossible, (int)IntStatus.Closed
    };

    private static readonly HashSet<int> TerminalIrpStatuses = new()
    {
        (int)IntStatus.Completed, (int)IntStatus.Skipped, (int)IntStatus.Failed, (int)IntStatus.Cancelled
    };

    private readonly IFixRequestsService _fixRequests;
    private readonly ICommentsService _comments;
    private readonly ITeamsService _teams;
    private readonly IUsersService _users;
    private readonly IIncidentResponsePlansService _irp;
    private readonly ILinksService _links;

    public SyncPushBuilder(ILogger logger, IDalService dalService,
        IFixRequestsService fixRequests, ICommentsService comments, ITeamsService teams,
        IUsersService users, IIncidentResponsePlansService irp, ILinksService links)
        : base(logger, dalService)
    {
        _fixRequests = fixRequests;
        _comments = comments;
        _teams = teams;
        _users = users;
        _irp = irp;
        _links = links;
    }

    public async Task<PushPayload> BuildBulkAsync()
    {
        var payload = new PushPayload { Cursor = DateTime.UtcNow.Ticks };

        var fixRequests = (await _fixRequests.GetAllFixRequestAsync())
            .Where(fr => !TerminalFixStatuses.Contains(fr.Status))
            .ToList();

        var teamIds = new HashSet<int>();
        var userIds = new HashSet<int>();

        foreach (var fr in fixRequests)
        {
            payload.FixRequests.Add(new FixRequestDto
            {
                Id = fr.Id,
                Identifier = fr.Identifier,
                VulnerabilityId = fr.VulnerabilityId,
                Status = fr.Status,
                IsTeamFix = fr.IsTeamFix ?? false,
                FixTeamId = fr.FixTeamId,
                SingleFixDestination = fr.SingleFixDestination,
                RequestingUserId = fr.RequestingUserId,
                VulnTitle = fr.Vulnerability?.Title ?? "",
                VulnDescription = fr.Vulnerability?.Description,
                VulnSolution = fr.Vulnerability?.Solution,
                VulnScore = fr.Vulnerability?.Score,
                HostName = fr.Vulnerability?.Host?.HostName
            });

            if (fr.FixTeamId != null) teamIds.Add(fr.FixTeamId.Value);
            if (fr.RequestingUserId != null) userIds.Add(fr.RequestingUserId.Value);
            if (fr.LastReportingUserId != null) userIds.Add(fr.LastReportingUserId.Value);

            var comments = await _comments.GetFixRequestCommentsAsync(fr.Id);
            payload.Comments.AddRange(comments.Select(c => new CommentDto
            {
                Id = c.Id, FixRequestId = fr.Id, UserId = c.UserId, IsAnonymous = c.IsAnonymous,
                CommenterName = c.CommenterName, Date = c.Date, Text = c.Text
            }));
        }

        foreach (var teamId in teamIds)
        {
            Team team;
            try { team = _teams.GetById(teamId, true); }
            catch { continue; }

            var teamDto = new TeamDto { Id = team.Value, Name = team.Name };
            foreach (var u in team.Users)
            {
                teamDto.Users.Add(new TeamUserDto { Value = u.Value, Login = u.Login });
                userIds.Add(u.Value);
            }
            payload.Teams.Add(teamDto);
        }

        foreach (var userId in userIds)
        {
            var user = _users.GetUserById(userId);
            if (user == null) continue;
            payload.Users.Add(new UserDto
            {
                Value = user.Value, Login = user.Login, Name = user.Name,
                Type = user.Type, Enabled = user.Enabled
            });
        }

        await AddIrpDataAsync(payload);

        return payload;
    }

    private async Task AddIrpDataAsync(PushPayload payload)
    {
        await using var db = DalService.GetContext();

        var executions = await db.IncidentResponsePlanTaskExecutions
            .Where(e => !TerminalIrpStatuses.Contains(e.Status))
            .ToListAsync();

        var taskIds = executions.Select(e => e.TaskId).Distinct().ToList();

        foreach (var exec in executions)
        {
            payload.IrpExecutions.Add(new IrpTaskExecutionDto
            {
                Id = exec.Id, TaskId = exec.TaskId, Status = exec.Status, CreatedAt = exec.CreatedAt
            });
        }

        foreach (var taskId in taskIds)
        {
            var task = await _irp.GetTaskByIdAsync(taskId);
            if (task == null) continue;

            int? incidentId = null;
            try
            {
                var incident = await _irp.GetIncidentByTaskIdAsync(taskId);
                if (incident != null)
                {
                    incidentId = incident.Id;
                    if (payload.Incidents.All(i => i.Id != incident.Id))
                        payload.Incidents.Add(new IncidentDto { Id = incident.Id, Name = incident.Name, Description = incident.Description });
                }
            }
            catch { /* incident not resolvable for this task — leave null */ }

            payload.IrpTasks.Add(new IrpTaskDto
            {
                Id = task.Id, PlanId = task.PlanId, IncidentId = incidentId, Name = task.Name,
                Description = task.Description, Notes = task.Notes, ConditionToProceed = task.ConditionToProceed,
                ConditionToSkip = task.ConditionToSkip, SuccessCriteria = task.SuccessCriteria,
                FailureCriteria = task.FailureCriteria, CompletionCriteria = task.CompletionCriteria,
                VerificationCriteria = task.VerificationCriteria
            });
        }
    }

    public Task<FastPushPayload> BuildFastAsync()
    {
        var payload = new FastPushPayload();
        var links = _links.GetLinks(PasswordResetLinkType);
        foreach (var link in links)
        {
            payload.Links.Add(new LinkDto
            {
                Id = link.Id,
                KeyHash = link.KeyHash,
                Type = link.Type,
                ExpirationDate = link.ExpirationDate,
                DataJson = link.Data != null ? Encoding.UTF8.GetString(link.Data) : null
            });
        }
        return Task.FromResult(payload);
    }
}
