using System.Text.Json;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using SyncContracts;

namespace ServerServices.Services;

public class SyncIngestService : ServiceBase, ISyncIngestService
{
    private readonly IFixRequestsService _fixRequests;
    private readonly ICommentsService _comments;
    private readonly IMessagesService _messages;
    private readonly IUsersService _users;
    private readonly ILinksService _links;
    private readonly IIncidentResponsePlansService _irp;

    public SyncIngestService(ILogger logger, IDalService dalService,
        IFixRequestsService fixRequests, ICommentsService comments, IMessagesService messages,
        IUsersService users, ILinksService links, IIncidentResponsePlansService irp)
        : base(logger, dalService)
    {
        _fixRequests = fixRequests;
        _comments = comments;
        _messages = messages;
        _users = users;
        _links = links;
        _irp = irp;
    }

    public async Task<List<Guid>> ApplyAsync(IEnumerable<OutboxActionDto> actions)
    {
        var applied = new List<Guid>();
        // Apply in chronological order so a status change and its comment land consistently.
        foreach (var action in actions.OrderBy(a => a.ActionTimeUtc))
        {
            if (await AlreadyProcessedAsync(action.ClientActionId))
            {
                applied.Add(action.ClientActionId); // re-ack so the website stops resending.
                continue;
            }

            try
            {
                await ApplyOneAsync(action);
                await RecordProcessedAsync(action);
                applied.Add(action.ClientActionId);
            }
            catch (SyncActionRejectedException ex)
            {
                // Refused for good — retrying cannot make a missing or mismatched key valid. Recorded and
                // acked so the website stops resending it; nothing was applied.
                Logger.Warning("Rejected sync action {Id} of type {Type}: {Reason}",
                    action.ClientActionId, action.ActionType, ex.Message);
                await RecordProcessedAsync(action);
                applied.Add(action.ClientActionId);
            }
            catch (Exception ex)
            {
                // Leave it un-acked; it will be retried on the next sync cycle.
                Logger.Error(ex, "Failed to apply sync action {Id} of type {Type}", action.ClientActionId, action.ActionType);
            }
        }
        return applied;
    }

    private async Task ApplyOneAsync(OutboxActionDto action)
    {
        switch (action.ActionType)
        {
            case SyncActionTypes.FixRequestStatusChange:
                await ApplyFixRequestStatusChangeAsync(Deserialize<FixRequestStatusChangeDto>(action));
                break;
            case SyncActionTypes.CommentCreate:
                await ApplyCommentAsync(Deserialize<CommentCreateDto>(action));
                break;
            case SyncActionTypes.MessageSend:
                var m = Deserialize<MessageSendDto>(action);
                await _messages.SendMessageAsync(m.Message, m.UserId, m.ChatId, m.Type);
                break;
            case SyncActionTypes.PasswordChange:
                ApplyPasswordChange(Deserialize<PasswordChangeDto>(action));
                break;
            case SyncActionTypes.LinkDelete:
                ApplyLinkDelete(Deserialize<LinkDeleteDto>(action));
                break;
            case SyncActionTypes.IrpTaskStatusChange:
                await ApplyIrpTaskStatusChangeAsync(Deserialize<IrpTaskStatusChangeDto>(action));
                break;
            default:
                Logger.Warning("Unknown sync action type {Type}", action.ActionType);
                break;
        }
    }

    /// <summary>The outcomes the public task report page can record (succeed, skip, fail).</summary>
    private static readonly HashSet<int> WebsiteReportableIrpStatuses = new()
    {
        (int)Model.IntStatus.Completed, (int)Model.IntStatus.Skipped, (int)Model.IntStatus.Failed
    };

    /// <summary>
    /// NR-2026-037, server half. The website already refuses a report without a valid key, but it is the
    /// public, less-trusted host, so the API does not take the execution id it sends on trust: the action
    /// must carry a key that still resolves to a live <see cref="IrpTaskReportLink"/> bound to that very
    /// execution, and the status must be one the page can report. The link is deleted once applied, so
    /// each key changes one execution once.
    /// </summary>
    private async Task ApplyIrpTaskStatusChangeAsync(IrpTaskStatusChangeDto dto)
    {
        if (!WebsiteReportableIrpStatuses.Contains(dto.NewStatus))
            throw new SyncActionRejectedException($"status {dto.NewStatus} cannot be reported from the website");

        if (!IrpTaskReportLink.IsWellFormedKey(dto.ReportKey))
            throw new SyncActionRejectedException("the report key is missing or malformed");

        byte[] data;
        try
        {
            data = _links.GetLinkData(IrpTaskReportLink.Type, dto.ReportKey);
        }
        catch (DataNotFoundException)
        {
            throw new SyncActionRejectedException("the report key names no live link");
        }

        IrpTaskReportLinkData? linkData;
        try
        {
            linkData = JsonSerializer.Deserialize<IrpTaskReportLinkData>(data);
        }
        catch (JsonException)
        {
            linkData = null;
        }

        if (linkData == null || linkData.TaskExecutionId != dto.TaskExecutionId)
            throw new SyncActionRejectedException("the report key is not bound to this task execution");

        await _irp.ChangeExecutionTaskSatusByIdAsync(dto.TaskExecutionId, dto.NewStatus, dto.ActionTimeUtc);

        try
        {
            _links.DeleteLink(IrpTaskReportLink.Type, dto.ReportKey);
        }
        catch (DataNotFoundException)
        {
            // Expired and cleaned between the read and the delete — the key is dead either way.
        }
    }

    private async Task ApplyFixRequestStatusChangeAsync(FixRequestStatusChangeDto dto)
    {
        var fixRequest = await _fixRequests.GetByIdAsync(dto.FixRequestId);
        fixRequest.Status = dto.NewStatus;
        fixRequest.LastInteraction = DateTime.Now;
        fixRequest.FixDate = dto.FixDate;
        if (dto.IsTeamFix)
        {
            if (dto.LastReportingUserId != null) fixRequest.LastReportingUserId = dto.LastReportingUserId;
        }
        else
        {
            fixRequest.SingleFixDestination = dto.SingleFixDestination;
        }
        await _fixRequests.SaveFixRequestAsync(fixRequest);

        if (fixRequest.RequestingUserId != null)
        {
            await _messages.SendMessageAsync(
                $"Your fix request #: {fixRequest.Id} of vulnerability #: {fixRequest.VulnerabilityId} has been updated status: {dto.NewStatusLabel}",
                fixRequest.RequestingUserId.Value, 1, 1);
        }
    }

    private async Task ApplyCommentAsync(CommentCreateDto dto)
    {
        await _comments.CreateCommentsAsync(
            dto.UserId, dto.Date, null, "FixRequest", dto.IsAnonymous, dto.CommenterName, dto.Text,
            dto.FixRequestId, null, null, null);
    }

    private void ApplyPasswordChange(PasswordChangeDto dto)
    {
        // Re-validate on the server (defense in depth); BCrypt is applied here, never on the website.
        if (!_users.CheckPasswordComplexity(dto.NewPassword))
            throw new Exception($"Password for user {dto.UserId} failed server-side complexity check");
        if (!_users.ChangePassword(dto.UserId, dto.NewPassword))
            throw new Exception($"ChangePassword returned false for user {dto.UserId}");
    }

    private void ApplyLinkDelete(LinkDeleteDto dto)
    {
        try
        {
            _links.DeleteLink(dto.Type, dto.Key);
        }
        catch (DataNotFoundException)
        {
            // Already gone (expired/cleaned) — treat as success so it gets acked.
        }
    }

    private async Task<bool> AlreadyProcessedAsync(Guid id)
    {
        await using var db = DalService.GetContext();
        return await db.ProcessedSyncActions.AnyAsync(x => x.ClientActionId == id.ToString());
    }

    private async Task RecordProcessedAsync(OutboxActionDto action)
    {
        await using var db = DalService.GetContext();
        db.ProcessedSyncActions.Add(new ProcessedSyncAction
        {
            ClientActionId = action.ClientActionId.ToString(),
            ActionType = action.ActionType,
            AppliedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static T Deserialize<T>(OutboxActionDto action) where T : class
        => JsonSerializer.Deserialize<T>(action.PayloadJson)
           ?? throw new Exception($"Invalid payload for action {action.ClientActionId}");

    /// <summary>An action that fails validation permanently: acked and recorded, never applied.</summary>
    private sealed class SyncActionRejectedException(string reason) : Exception(reason);
}
