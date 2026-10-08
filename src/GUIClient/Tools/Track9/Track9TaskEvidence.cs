using DAL.Entities;
using Model.Governance;
namespace GUIClient.Tools.Track9;
public static class Track9TaskEvidence
{
    public static MitigationTaskRequest Update(MitigationTask task, string? criterion, string? evidence) => new()
    {
        Id = task.Id, MitigationId = task.MitigationId, Title = task.Title,
        Description = task.Description, OwnerId = task.OwnerId, DueDate = task.DueDate,
        Status = task.Status, AcceptanceCriterion = criterion, CompletionEvidence = evidence
    };
}
