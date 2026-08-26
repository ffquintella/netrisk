using DAL.Entities;
using Model.Governance;

namespace ServerServices.Interfaces;

/// <summary>
/// POA&amp;M-style treatment task line items (Track 8 milestone 8.5.3).
///
/// A <c>Mitigation</c> carries one planning date and one percentage, which cannot express who is
/// doing what by when. These rows can, and they are what the business portal creates when a reviewer
/// asks for mitigation instead of accepting a risk.
/// </summary>
public interface IMitigationTasksService
{
    Task<List<MitigationTask>> GetByMitigationAsync(int mitigationId);

    Task<List<MitigationTask>> GetByRiskAsync(int riskId);

    Task<MitigationTask> GetAsync(int id);

    Task<MitigationTask> CreateAsync(MitigationTaskRequest request, int actingUserId);

    Task<MitigationTask> UpdateAsync(MitigationTaskRequest request, int actingUserId);

    Task DeleteAsync(int id);

    /// <summary>
    /// Tasks that are overdue or due within <paramref name="withinDays"/>, for the notification job.
    /// Completed and cancelled tasks are excluded: they are not work anybody needs chasing.
    /// </summary>
    Task<List<MitigationTask>> GetDueOrOverdueAsync(DateTime asOfUtc, int withinDays);

    /// <summary>Records that a task's owner has been told, so the daily job does not repeat itself.</summary>
    Task MarkNotifiedAsync(int taskId, int daysBefore);
}
