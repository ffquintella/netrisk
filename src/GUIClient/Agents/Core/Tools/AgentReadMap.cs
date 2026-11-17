using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using Model;
using Model.Assessments;
using Model.DTO;

namespace GUIClient.Agents.ReadTools;

/// <summary>
/// The hand-written mappings from ClientServices answers to the result DTOs. Every string goes through
/// <see cref="Title"/> or <see cref="Text"/>; entity properties go through <see cref="AgentDataPolicy.Screen"/>.
/// </summary>
public static class AgentReadMap
{
    public static string? Title(string? value) => AgentResultLimits.Clip(value, AgentResultLimits.TitleLength);

    public static string? Text(string? value) => AgentResultLimits.Clip(value, AgentResultLimits.TextLength);

    private static string Required(string? value) => Title(value) ?? "";

    public static AgentRiskSummary RiskSummary(Risk r) => new(r.Id, Required(r.Subject), Required(r.Status),
        Title(r.ReferenceId), Title(r.CategoryNavigation?.Name), r.Owner, Title(r.OwnerUser?.Name), r.SubmissionDate,
        r.LastUpdate, r.EntityId, r.ReviewRequested);

    public static AgentRiskDetail RiskDetail(Risk r, RiskScoring? s) => new(r.Id, Required(r.Subject), Required(r.Status),
        Title(r.ReferenceId), Title(r.ControlNumber), Title(r.CategoryNavigation?.Name), Title(r.SourceNavigation?.Name),
        r.Owner, Title(r.OwnerUser?.Name), r.Manager, Title(r.ManagerUser?.Name), r.SubmissionDate, r.LastUpdate,
        Text(r.Assessment), Text(r.Notes), Text(r.ScenarioCause), Text(r.ScenarioVulnerability),
        Text(r.ScenarioCentralEvent), Text(r.ScenarioConsequences), r.EntityId, r.MitigationId, r.IncidentResponsePlanId,
        r.ReviewRequested, Text(r.ReviewRequestedReason),
        s is null ? null : new AgentRiskScore(s.ScoringMethod, s.CalculatedRisk, s.ClassicLikelihood, s.ClassicImpact,
            s.ResidualRisk, s.ContributingScore));

    public static AgentRiskMitigation Mitigation(Mitigation m) => new(m.Id, m.RiskId, m.SubmissionDate, m.LastUpdate,
        Title(m.PlanningStrategyNavigation?.Name), Title(m.MitigationEffortNavigation?.Name),
        Title(m.MitigationCostNavigation?.Name), m.MitigationOwner, Title(m.MitigationOwnerNavigation?.Name),
        Text(m.CurrentSolution), Text(m.SecurityRequirements), Text(m.SecurityRecommendations), m.PlanningDate,
        m.MitigationPercent);

    public static AgentRiskReview Review(MgmtReview r) => new(r.Id, r.SubmissionDate, Title(r.ReviewNavigation?.Name),
        r.Reviewer, Title(r.ReviewerNavigation?.Name), Title(r.NextStepNavigation?.Name), r.NextReview, Text(r.Comments));

    public static string? StatusName(int status) =>
        Enum.IsDefined(typeof(IntStatus), status) ? ((IntStatus)status).ToString() : null;

    public static AgentFindingSummary FindingSummary(Vulnerability v) => new(v.Id, Required(v.Title), Title(v.Severity),
        v.Score, v.Status, StatusName(v.Status), v.FirstDetection, v.LastDetection, v.DetectionCount, v.HostId, v.EntityId,
        v.FixTeamId, Title(v.Technology), Title(v.Cves));

    public static AgentFindingDetail FindingDetail(Vulnerability v) => new(v.Id, Required(v.Title), Title(v.Severity),
        v.Score, v.Status, StatusName(v.Status), v.FirstDetection, v.LastDetection, v.DetectionCount, v.HostId,
        v.HostServiceId, v.EntityId, v.FixTeamId, v.AnalystId, Title(v.Technology), Title(v.Cves), Text(v.Description),
        Text(v.Solution), Text(v.Details), Text(v.Comments), Title(v.ImportSource), v.Cvss3BaseScore, Title(v.Cvss3Vector),
        v.CvssBaseScore, v.ExploitAvaliable, v.PatchPublicationDate, v.VulnerabilityPublicationDate);

    public static AgentFindingLifecycleEntry LifecycleEntry(FindingStatusHistory h) => new(h.ChangedAt,
        h.FromStatus?.ToString(), h.ToStatus.ToString(), h.Source.ToString(), h.UserId, Text(h.Justification),
        h.DuplicateOfId);

    public static string IncidentCode(Incident i) => $"{i.Year}-{i.Sequence}";

    public static AgentIncidentSummary IncidentSummary(Incident i) => new(i.Id, IncidentCode(i), Required(i.Name),
        Title(i.Category), i.Kind.ToString(), i.Status, i.ReportDate, i.StartDate, i.LastUpdate, i.AssignedToId,
        i.ImpactedEntityId);

    public static AgentIncidentDetail IncidentDetail(Incident i, IReadOnlyList<int> planIds, int timelinePage,
        int perPage) =>
        new(i.Id, IncidentCode(i), Required(i.Name), Title(i.Category), i.Kind.ToString(), i.Status, i.ReportDate,
            i.StartDate, i.CreationDate, i.LastUpdate, i.Duration, i.AssignedToId, i.EntityId, i.ImpactedEntityId,
            i.ReportEntityId, Title(i.ReportedBy), Text(i.Description), Text(i.Report), Text(i.Impact), Text(i.Cause),
            Text(i.Solution), Text(i.Recommendations), Text(i.Notes), planIds.Take(AgentResultLimits.MaxNestedItems).ToList(),
            AgentPage<AgentTimelineEntry>.Of(IncidentTimeline(i), timelinePage, perPage, e => e));

    /// <summary>The incident's dated events and recorded actions, oldest first — what the incident record holds.</summary>
    private static IEnumerable<AgentTimelineEntry> IncidentTimeline(Incident i)
    {
        var entries = new List<AgentTimelineEntry>
        {
            new(i.CreationDate, "created", i.CreatedById, null),
            new(i.ReportDate, "reported", i.ReportedById, null)
        };
        if (i.StartDate is { } start) entries.Add(new AgentTimelineEntry(start, "started", null, null));
        if (i.LastUpdate != default) entries.Add(new AgentTimelineEntry(i.LastUpdate, "updated", i.UpdatedById, null));
        entries.AddRange((i.Actions ?? Array.Empty<NrAction>())
            .Select(a => new AgentTimelineEntry(a.DateTime, "action", a.UserId, Text(a.Message))));
        return entries.OrderBy(e => e.At);
    }

    public static AgentResponsePlanSummary PlanSummary(IncidentResponsePlan p) => new(p.Id, Required(p.Name), p.Status,
        p.CreationDate, p.LastUpdate, p.HasBeenTested, p.HasBeenApproved, p.LastTestDate, p.ApprovalDate);

    public static AgentResponsePlanDetail PlanDetail(IncidentResponsePlan p,
        IEnumerable<IncidentResponsePlanExecution>? runs, int taskPage, int executionPage, int perPage) =>
        new(p.Id, Required(p.Name), p.Status, Text(p.Description), Text(p.Notes), p.CreationDate, p.LastUpdate,
            p.HasBeenTested, p.HasBeenExercised, p.HasBeenReviewed, p.HasBeenApproved, p.LastTestDate, p.LastExerciseDate,
            p.LastReviewDate, p.ApprovalDate,
            AgentPage<AgentResponsePlanTask>.Of((p.Tasks ?? Array.Empty<IncidentResponsePlanTask>())
                .OrderBy(t => t.ExecutionOrder).ThenBy(t => t.Id), taskPage, perPage, PlanTask),
            AgentPage<AgentResponsePlanExecution>.Of((runs ?? Array.Empty<IncidentResponsePlanExecution>())
                .OrderByDescending(e => e.ExecutionDate).ThenBy(e => e.Id), executionPage, perPage,
                PlanExecution));

    private static AgentResponsePlanTask PlanTask(IncidentResponsePlanTask t) => new(t.Id, Required(t.Name),
        Text(t.Description), t.Status, t.ExecutionOrder, t.Priority, Title(t.TaskType), t.IsOptional, t.EstimatedDuration,
        t.AssignedToId, Text(t.SuccessCriteria), Text(t.CompletionCriteria));

    private static AgentResponsePlanExecution PlanExecution(IncidentResponsePlanExecution e) => new(e.Id, e.ExecutionDate,
        e.Status, e.Duration, e.IsTest, e.IsExercise, Title(e.ExecutionTrigger), Text(e.ExecutionResult), Text(e.Notes));

    public static AgentAssessmentSummary Assessment(Assessment a) => new(a.Id, Required(a.Name), a.Created, a.EntityId);

    public static AgentAssessmentRun AssessmentRun(AssessmentRun r) => new(r.Id, r.AssessmentId, r.EntityId, r.HostId,
        r.RunDate, r.Status, r.ProgressPercentage, r.AnalystId, Text(r.Comments));

    public static AgentAssessmentAnswer RunAnswer(AssessmentRunsAnswer a) => new(a.QuestionId,
        Text(a.Question?.Question), a.AnswerId, Text(a.Answer?.Answer));

    public static AgentAssessmentEvidence Evidence(AssessmentAnswerEvidence e) => new(e.QuestionId, Required(e.Name),
        Title(e.Type), e.Size, e.Timestamp);

    /// <summary>The entity's name, unless the data policy withholds it (a name that reads like a secret).</summary>
    public static string? EntityName(Entity e)
    {
        var name = e.EntitiesProperties?.FirstOrDefault(p => p.Type == "name")?.Value;
        return AgentDataPolicy.Screen("name", name) is null ? Title(name) : null;
    }

    public static AgentEntitySummary EntitySummary(Entity e) => new(e.Id, Required(e.DefinitionName), EntityName(e),
        Title(e.Status), e.Parent, e.Updated);

    public static AgentEntityRef EntityRef(Entity e) => new(e.Id, Required(e.DefinitionName), EntityName(e));

    public static AgentEntityDetail EntityDetail(Entity e, AgentWithholding withheld)
    {
        var properties = (e.EntitiesProperties ?? Array.Empty<EntitiesProperty>()).ToList();
        var kept = new List<EntitiesProperty>();
        foreach (var property in properties)
        {
            if (AgentDataPolicy.Screen(property.Type, property.Value) is { } kind) withheld.Add(kind);
            else kept.Add(property);
        }
        return new AgentEntityDetail(e.Id, Required(e.DefinitionName), EntityName(e), Title(e.Status), e.Parent, e.Created,
            e.Updated, AgentCappedList<AgentEntityProperty>.Of(kept, p => new AgentEntityProperty(Required(p.Type), Text(p.Value))),
            properties.Count - kept.Count);
    }

    public static AgentHostSummary HostSummary(Host h) => new(h.Id, Title(h.HostName), Title(h.Ip), Title(h.Fqdn),
        Title(h.Os), h.Status, Title(h.Source), h.TeamId, h.EntityId, h.RegistrationDate, h.LastVerificationDate);

    public static AgentHostDetail HostDetail(Host h, IEnumerable<HostsService>? services, HostVulnerabilitySummaryDto? summary) =>
        new(h.Id, Title(h.HostName), Title(h.Ip), Title(h.Fqdn), Title(h.Os), Title(h.MacAddress), h.Status, Title(h.Source),
            h.TeamId, h.EntityId, h.RegistrationDate, h.LastVerificationDate, Text(h.Comment),
            AgentCappedList<AgentHostService>.Of(services,
                s => new AgentHostService(s.Id, Required(s.Name), Title(s.Protocol), s.Port)),
            summary?.Open is { } open
                ? new AgentSeverityCounts(open.Critical, open.High, open.Medium, open.Low, open.None)
                : null,
            summary?.Total);
}
