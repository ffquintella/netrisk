using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Governance;
using Model.TreatmentEconomics;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Governance;

/// <summary>
/// Track 8 milestone 8.5.3 — mitigation task line items.
///
/// NIST RMF documents remediation as a plan of action and milestones: tasks, owners, dates. ISO
/// auditors ask for a treatment plan with timelines, responsibilities and status. NetRisk had a
/// percentage and a single date, which answers none of that.
/// </summary>
public class MitigationTasksService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IMitigationTasksService
{
    public async Task<List<MitigationTask>> GetByMitigationAsync(int mitigationId)
    {
        await using var db = DalService.GetContext();

        return await db.MitigationTasks
            .Where(t => t.MitigationId == mitigationId)
            .Include(t => t.Owner)
            .OrderBy(t => t.Status)
            .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenBy(t => t.Id)
            .ToListAsync();
    }

    public async Task<List<MitigationTask>> GetByRiskAsync(int riskId)
    {
        await using var db = DalService.GetContext();

        var mitigationIds = await db.Mitigations.Where(m => m.RiskId == riskId).Select(m => m.Id)
            .ToListAsync();

        return await db.MitigationTasks
            .Where(t => mitigationIds.Contains(t.MitigationId))
            .Include(t => t.Owner)
            .OrderBy(t => t.Status)
            .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenBy(t => t.Id)
            .ToListAsync();
    }

    public async Task<MitigationTask> GetAsync(int id)
    {
        await using var db = DalService.GetContext();

        return await db.MitigationTasks.Include(t => t.Owner).FirstOrDefaultAsync(t => t.Id == id)
               ?? throw new DataNotFoundException("local", "mitigation_tasks",
                   new Exception($"Mitigation task with id {id} not found"));
    }

    public async Task<MitigationTask> CreateAsync(MitigationTaskRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidParameterException(nameof(request.Title),
                "A task needs a title. 'Do the mitigation' is not a plan of action.");

        await using var db = DalService.GetContext();

        if (!await db.Mitigations.AnyAsync(m => m.Id == request.MitigationId))
            throw new DataNotFoundException("local", "mitigations",
                new Exception($"Mitigation with id {request.MitigationId} not found"));

        await EnsureOwnerExistsAsync(db, request.OwnerId);

        var criterion = Text(request.AcceptanceCriterion, nameof(MitigationTaskRequest.AcceptanceCriterion));
        var evidence = Text(request.CompletionEvidence, nameof(MitigationTaskRequest.CompletionEvidence));
        var status = request.Status ?? MitigationTaskStatus.Open;

        // Stage 9.6 (S47 §4.8): a task is done when there is evidence it is done.
        if (status == MitigationTaskStatus.Completed && evidence is null) throw EvidenceRequired();

        var now = DateTime.UtcNow;

        var task = new MitigationTask
        {
            MitigationId = request.MitigationId,
            Title = request.Title.Trim(),
            Description = request.Description,
            OwnerId = request.OwnerId,
            DueDate = request.DueDate,
            Status = status,
            CreatedAt = now,
            CreatedById = actingUserId,
            AcceptanceCriterion = criterion
        };

        if (evidence is not null) RecordEvidence(task, evidence, actingUserId, now);

        if (task.Status == MitigationTaskStatus.Completed) task.CompletedAt = now;

        db.MitigationTasks.Add(task);
        await db.SaveChangesAsync();

        Logger.Information("Mitigation task {Id} '{Title}' created on mitigation {Mitigation}", task.Id,
            task.Title, task.MitigationId);

        return task;
    }

    public async Task<MitigationTask> UpdateAsync(MitigationTaskRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var db = DalService.GetContext();

        var task = await db.MitigationTasks.FirstOrDefaultAsync(t => t.Id == request.Id)
                   ?? throw new DataNotFoundException("local", "mitigation_tasks",
                       new Exception($"Mitigation task with id {request.Id} not found"));

        await EnsureOwnerExistsAsync(db, request.OwnerId);

        // Stage 9.6 (S47 §4.8, D12): the two Phase 5 fields follow "null leaves it as it is, blank clears it", so a
        // client that does not know them — every client written before schema 95 — cannot erase them.
        var criterion = Text(request.AcceptanceCriterion, nameof(MitigationTaskRequest.AcceptanceCriterion));
        var evidence = Text(request.CompletionEvidence, nameof(MitigationTaskRequest.CompletionEvidence));
        var clearsEvidence = request.CompletionEvidence is not null && evidence is null;
        var targetStatus = request.Status ?? task.Status;
        var now = DateTime.UtcNow;

        if (targetStatus == MitigationTaskStatus.Completed)
        {
            if (clearsEvidence)
                throw new InvalidParameterException(nameof(MitigationTaskRequest.CompletionEvidence),
                    "The evidence of a completed task cannot be removed — it is what the completion rests on. Reopen " +
                    "the task first if the evidence was wrong.");

            if (evidence is null && string.IsNullOrWhiteSpace(task.CompletionEvidence) && task.Status != targetStatus)
                throw EvidenceRequired();
        }

        if (!string.IsNullOrWhiteSpace(request.Title)) task.Title = request.Title.Trim();
        task.Description = request.Description;
        task.OwnerId = request.OwnerId;
        task.DueDate = request.DueDate;

        if (request.AcceptanceCriterion is not null) task.AcceptanceCriterion = criterion;

        if (evidence is not null && evidence != task.CompletionEvidence)
            RecordEvidence(task, evidence, actingUserId, now);
        else if (clearsEvidence)
        {
            task.CompletionEvidence = null;
            task.CompletionEvidenceAt = null;
            task.CompletionEvidenceById = null;
        }

        if (request.Status is not null && request.Status != task.Status)
        {
            task.Status = request.Status.Value;

            // The completion timestamp follows the status rather than being sent by the client: a
            // caller that can set "completed at" independently of "completed" can date the work
            // whenever it likes, and this row is evidence.
            task.CompletedAt = task.Status == MitigationTaskStatus.Completed ? DateTime.UtcNow : null;

            // A task that moves back into play starts its notification clock again.
            if (task.Status is MitigationTaskStatus.Open or MitigationTaskStatus.InProgress)
                task.LastNotifiedDaysBefore = null;
        }

        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return task;
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = DalService.GetContext();

        var task = await db.MitigationTasks.FirstOrDefaultAsync(t => t.Id == id)
                   ?? throw new DataNotFoundException("local", "mitigation_tasks",
                       new Exception($"Mitigation task with id {id} not found"));

        db.MitigationTasks.Remove(task);
        await db.SaveChangesAsync();
    }

    public async Task<List<MitigationTask>> GetDueOrOverdueAsync(DateTime asOfUtc, int withinDays)
    {
        await using var db = DalService.GetContext();

        var horizon = asOfUtc.AddDays(withinDays);

        return await db.MitigationTasks
            .Where(t => t.DueDate != null &&
                        t.DueDate <= horizon &&
                        (t.Status == MitigationTaskStatus.Open || t.Status == MitigationTaskStatus.InProgress))
            .Include(t => t.Owner)
            .Include(t => t.Mitigation)
            .OrderBy(t => t.DueDate)
            .ToListAsync();
    }

    public async Task MarkNotifiedAsync(int taskId, int daysBefore)
    {
        await using var db = DalService.GetContext();

        var task = await db.MitigationTasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return;

        task.LastNotifiedDaysBefore = daysBefore;
        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    /// <summary>Trimmed text, null when blank; refused above <see cref="TreatmentEconomicsLimits.MaxTaskTextLength"/>.</summary>
    private static string? Text(string? value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        if (trimmed.Length > TreatmentEconomicsLimits.MaxTaskTextLength)
            throw new InvalidParameterException(parameter,
                $"{parameter} is at most {TreatmentEconomicsLimits.MaxTaskTextLength} characters.");

        return trimmed;
    }

    /// <summary>Stamps who recorded the evidence and when, from the server — never from the request.</summary>
    private static void RecordEvidence(MitigationTask task, string evidence, int actingUserId, DateTime now)
    {
        task.CompletionEvidence = evidence;
        task.CompletionEvidenceAt = now;
        task.CompletionEvidenceById = actingUserId > 0 ? actingUserId : null;
    }

    private static InvalidParameterException EvidenceRequired() =>
        new(nameof(MitigationTaskRequest.CompletionEvidence),
            "A task is completed with evidence that it was done — a description, a reference or a link (MIGR-TI/IA " +
            "Phase 5). Send the completion evidence with the status.");

    private static async Task EnsureOwnerExistsAsync(DAL.Context.AuditableContext db, int? ownerId)
    {
        if (ownerId is null) return;
        if (await db.Users.AnyAsync(u => u.Value == ownerId.Value)) return;

        throw new DataNotFoundException("local", "user", new Exception($"User with id {ownerId} not found"));
    }
}
