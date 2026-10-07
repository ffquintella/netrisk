using DAL.Entities;

namespace ServerServices.Security;

/// <summary>
/// The parent records an attachment can hang off, read from its foreign-key columns (security findings
/// NR-2026-017 and NR-2026-035).
///
/// One place, so the read rule (<see cref="FileAccessAuthorizer.EnsureCanReadAsync"/>), the write rule
/// (<see cref="FileAccessAuthorizer.EnsureCanAttachAsync"/>) and the files service's own refusal of a
/// second parent cannot disagree about which columns are parents. A parent column added to
/// <see cref="NrFile"/> and not listed here is a column nobody authorizes — add it here first.
/// </summary>
public static class FileParents
{
    public enum Kind
    {
        Risk,
        Mitigation,
        RiskAcceptance,
        Incident,
        IncidentResponsePlan,
        IncidentResponsePlanExecution,
        IncidentResponsePlanTask,
        IncidentResponsePlanTaskExecution,
        AssessmentRunAnswer
    }

    public readonly record struct Parent(Kind Kind, int Id);

    /// <summary>Every parent FK the file carries, in the order the columns were added.</summary>
    public static List<Parent> Declared(NrFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var parents = new List<Parent>(1);

        if (file.RiskId is { } risk) parents.Add(new Parent(Kind.Risk, risk));
        if (file.MitigationId is { } mitigation) parents.Add(new Parent(Kind.Mitigation, mitigation));
        if (file.RiskAcceptanceId is { } acceptance) parents.Add(new Parent(Kind.RiskAcceptance, acceptance));
        if (file.IncidentId is { } incident) parents.Add(new Parent(Kind.Incident, incident));
        if (file.IncidentResponsePlanId is { } plan) parents.Add(new Parent(Kind.IncidentResponsePlan, plan));
        if (file.IncidentResponsePlanExecutionId is { } planExecution)
            parents.Add(new Parent(Kind.IncidentResponsePlanExecution, planExecution));
        if (file.IncidentResponsePlanTaskId is { } task) parents.Add(new Parent(Kind.IncidentResponsePlanTask, task));
        if (file.IncidentResponsePlanTaskExecutionId is { } taskExecution)
            parents.Add(new Parent(Kind.IncidentResponsePlanTaskExecution, taskExecution));
        if (file.AssessmentRunAnswerId is { } answer) parents.Add(new Parent(Kind.AssessmentRunAnswer, answer));

        return parents;
    }
}
