using Microsoft.EntityFrameworkCore;
using SyncContracts;
using WebSiteData.Entities;
using WebSiteData.Services;

namespace WebSiteData.Sync;

public interface ISyncApplyService
{
    Task<bool> TryEnrollAsync(EnrollRequest request);
    Task RotateKeyAsync(RotateKeyRequest request);
    Task<string?> GetEnrolledPublicKeyAsync();
    Task<SyncResponse> ApplyPushAsync(PushPayload payload);
    Task<SyncResponse> ApplyFastAsync(FastPushPayload payload);
    Task ApplyAckAsync(AckRequest ack);
}

/// <summary>
/// Applies inbound signed sync payloads to the local SQLite store and produces the outbox
/// batch the server pulls. Read tables use transactional full-replace; links are upserted.
/// </summary>
public class SyncApplyService : ISyncApplyService
{
    private readonly IDbContextFactory<WebSiteDbContext> _factory;
    private readonly IOutboxService _outbox;

    public SyncApplyService(IDbContextFactory<WebSiteDbContext> factory, IOutboxService outbox)
    {
        _factory = factory;
        _outbox = outbox;
    }

    public async Task<bool> TryEnrollAsync(EnrollRequest request)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var state = await db.SyncState.FirstOrDefaultAsync();
        if (state != null && !string.IsNullOrEmpty(state.ApiPublicKeyPem))
            return false; // TOFU: already enrolled.

        if (state == null)
        {
            state = new SyncState { Id = 1 };
            db.SyncState.Add(state);
        }
        state.ApiKeyId = request.KeyId;
        state.ApiPublicKeyPem = request.PublicKeyPem;
        state.EnrolledAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task RotateKeyAsync(RotateKeyRequest request)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var state = await db.SyncState.FirstOrDefaultAsync();
        if (state == null)
        {
            state = new SyncState { Id = 1 };
            db.SyncState.Add(state);
        }
        state.ApiKeyId = request.NewKeyId;
        state.ApiPublicKeyPem = request.NewPublicKeyPem;
        await db.SaveChangesAsync();
    }

    public async Task<string?> GetEnrolledPublicKeyAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var state = await db.SyncState.AsNoTracking().FirstOrDefaultAsync();
        return state?.ApiPublicKeyPem;
    }

    public async Task<SyncResponse> ApplyPushAsync(PushPayload payload)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        // Full-replace of the read-only display tables.
        await db.FixRequests.ExecuteDeleteAsync();
        await db.Comments.ExecuteDeleteAsync();
        await db.Teams.ExecuteDeleteAsync();
        await db.TeamUsers.ExecuteDeleteAsync();
        await db.Users.ExecuteDeleteAsync();
        await db.IrpTaskExecutions.ExecuteDeleteAsync();
        await db.IrpTasks.ExecuteDeleteAsync();
        await db.Incidents.ExecuteDeleteAsync();

        db.FixRequests.AddRange(payload.FixRequests.Select(MapFixRequest));
        db.Comments.AddRange(payload.Comments.Select(MapComment));
        db.Incidents.AddRange(payload.Incidents.Select(c => new LocalIncident { Id = c.Id, Name = c.Name, Description = c.Description }));
        db.IrpTasks.AddRange(payload.IrpTasks.Select(MapTask));
        db.IrpTaskExecutions.AddRange(payload.IrpExecutions.Select(e =>
            new LocalIrpTaskExecution { Id = e.Id, TaskId = e.TaskId, Status = e.Status, CreatedAt = e.CreatedAt }));

        foreach (var team in payload.Teams)
        {
            db.Teams.Add(new LocalTeam { Id = team.Id, Name = team.Name });
            foreach (var u in team.Users)
                db.TeamUsers.Add(new LocalTeamUser { TeamId = team.Id, UserValue = u.Value, Login = u.Login });
        }
        db.Users.AddRange(payload.Users.Select(u => new LocalUser
        {
            Value = u.Value, Login = u.Login, Name = u.Name, Type = u.Type, Enabled = u.Enabled
        }));

        var state = await db.SyncState.FirstOrDefaultAsync();
        if (state == null) { state = new SyncState { Id = 1 }; db.SyncState.Add(state); }
        state.BulkCursor = payload.Cursor;

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return await BuildPendingResponseAsync(state.BulkCursor);
    }

    public async Task<SyncResponse> ApplyFastAsync(FastPushPayload payload)
    {
        await using var db = await _factory.CreateDbContextAsync();

        if (payload.DeletedLinkIds.Count > 0)
        {
            var del = payload.DeletedLinkIds.ToHashSet();
            await db.Links.Where(l => del.Contains(l.Id)).ExecuteDeleteAsync();
        }

        var pushedIds = payload.Links.Select(l => l.Id).ToHashSet();
        var pushedTypes = payload.Links.Select(l => l.Type).Distinct().ToHashSet();

        foreach (var dto in payload.Links)
        {
            var existing = await db.Links.FirstOrDefaultAsync(l => l.Id == dto.Id);
            if (existing == null)
            {
                db.Links.Add(new LocalLink
                {
                    Id = dto.Id, KeyHash = dto.KeyHash, Type = dto.Type,
                    ExpirationDate = dto.ExpirationDate, DataJson = dto.DataJson
                });
            }
            else
            {
                // Preserve the local Consumed flag — never resurrect a used link.
                existing.KeyHash = dto.KeyHash;
                existing.Type = dto.Type;
                existing.ExpirationDate = dto.ExpirationDate;
                existing.DataJson = dto.DataJson;
            }
        }

        // Prune links of the managed types that the server no longer lists and that were not
        // consumed locally (deleted/expired server-side). Consumed rows are kept as inert.
        var stale = await db.Links
            .Where(l => pushedTypes.Contains(l.Type) && !pushedIds.Contains(l.Id) && !l.Consumed)
            .ToListAsync();
        db.Links.RemoveRange(stale);

        await db.SaveChangesAsync();

        long cursor;
        await using (var db2 = await _factory.CreateDbContextAsync())
            cursor = (await db2.SyncState.AsNoTracking().FirstOrDefaultAsync())?.BulkCursor ?? 0;

        return await BuildPendingResponseAsync(cursor);
    }

    public Task ApplyAckAsync(AckRequest ack) => _outbox.MarkAckedAsync(ack.AckedActionIds);

    private async Task<SyncResponse> BuildPendingResponseAsync(long cursor)
    {
        var pending = await _outbox.GetPendingAsync();
        var response = new SyncResponse
        {
            AppliedCursor = cursor,
            Actions = pending.Select(p => new OutboxActionDto
            {
                ClientActionId = p.ClientActionId,
                ActionType = p.ActionType,
                PayloadJson = p.PayloadJson,
                ActionTimeUtc = p.ActionTimeUtc
            }).ToList()
        };
        await _outbox.MarkSentAsync(pending.Select(p => p.ClientActionId));
        return response;
    }

    private static LocalFixRequest MapFixRequest(FixRequestDto d) => new()
    {
        Id = d.Id, Identifier = d.Identifier, VulnerabilityId = d.VulnerabilityId, Status = d.Status,
        IsTeamFix = d.IsTeamFix, FixTeamId = d.FixTeamId, SingleFixDestination = d.SingleFixDestination,
        RequestingUserId = d.RequestingUserId, VulnTitle = d.VulnTitle, VulnDescription = d.VulnDescription,
        VulnSolution = d.VulnSolution, VulnScore = d.VulnScore, HostName = d.HostName
    };

    private static LocalComment MapComment(CommentDto d) => new()
    {
        Id = d.Id, FixRequestId = d.FixRequestId, UserId = d.UserId, IsAnonymous = d.IsAnonymous,
        CommenterName = d.CommenterName, Date = d.Date, Text = d.Text
    };

    private static LocalIrpTask MapTask(IrpTaskDto d) => new()
    {
        Id = d.Id, PlanId = d.PlanId, IncidentId = d.IncidentId, Name = d.Name, Description = d.Description,
        Notes = d.Notes, ConditionToProceed = d.ConditionToProceed, ConditionToSkip = d.ConditionToSkip,
        SuccessCriteria = d.SuccessCriteria, FailureCriteria = d.FailureCriteria,
        CompletionCriteria = d.CompletionCriteria, VerificationCriteria = d.VerificationCriteria
    };
}
