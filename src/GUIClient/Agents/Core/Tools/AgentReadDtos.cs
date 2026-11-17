using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using Model;
using Model.Assessments;
using Model.DTO;

namespace GUIClient.Agents.ReadTools;

// The result shapes of the read tools (T351, T352). Each is an allowlist: a record naming exactly the fields an agent
// may see, mapped by hand from the ClientServices answer. No member is an entity, an object, a dictionary or a byte
// array, and none is named like a credential — AgentResultShape checks every tool's ResultType at registration and
// every result before it is sent. Every string is clipped with a visible marker (AgentResultLimits); lists of long-text
// items inside one item are paged (AgentResultLimits.NestedPageSize). An item an edit tool can change also carries its
// Version (AgentVersions, M63): the token the edit tool checks so a stale write is refused.

public sealed record AgentWhoAmI(string UserName, IReadOnlyList<string> Roles);

public sealed record AgentRiskSummary(int Id, string Subject, string Status, string? ReferenceId, string? Category,
    int? OwnerId, string? OwnerName, DateTime SubmissionDate, DateTime LastUpdate, int? EntityId, bool ReviewRequested);

public sealed record AgentRiskScore(int ScoringMethod, double CalculatedRisk, double ClassicLikelihood,
    double ClassicImpact, double? ResidualRisk, double? ContributingScore);

public sealed record AgentRiskDetail(int Id, string Subject, string Status, string? ReferenceId, string? ControlNumber,
    string? Category, string? Source, int? OwnerId, string? OwnerName, int? ManagerId, string? ManagerName,
    DateTime SubmissionDate, DateTime LastUpdate, string? Assessment, string? Notes, string? ScenarioCause,
    string? ScenarioVulnerability, string? ScenarioCentralEvent, string? ScenarioConsequences, int? EntityId,
    int? MitigationId, int? IncidentResponsePlanId, bool ReviewRequested, string? ReviewRequestedReason,
    AgentRiskScore? Score, string Version);

/// <summary>One entry of a NetRisk list an edit tool takes an id from (T354).</summary>
public sealed record AgentOption(int Id, string Name);

/// <summary>The risk categories and sources create_risk takes ids from (T354).</summary>
public sealed record AgentRiskOptions(IReadOnlyList<AgentOption> Categories, IReadOnlyList<AgentOption> Sources);

public sealed record AgentRiskMitigation(int Id, int RiskId, DateTime SubmissionDate, DateTime LastUpdate,
    string? PlanningStrategy, string? Effort, string? Cost, int OwnerId, string? OwnerName, string? CurrentSolution,
    string? SecurityRequirements, string? SecurityRecommendations, DateOnly PlanningDate, int PercentComplete);

public sealed record AgentRiskReview(int Id, DateTime SubmissionDate, string? Review, int ReviewerId, string? ReviewerName,
    string? NextStep, DateOnly NextReview, string? Comments);

public sealed record AgentRiskReviews(int RiskId, AgentPage<AgentRiskReview> Reviews);

public sealed record AgentFindingSummary(int Id, string Title, string? Severity, double? Score, int StatusId,
    string? Status, DateTime FirstDetection, DateTime LastDetection, int DetectionCount, int? HostId, int? EntityId,
    int? FixTeamId, string? Technology, string? Cves);

public sealed record AgentFindingDetail(int Id, string Title, string? Severity, double? Score, int StatusId, string? Status,
    DateTime FirstDetection, DateTime LastDetection, int DetectionCount, int? HostId, int? HostServiceId, int? EntityId,
    int? FixTeamId, int? AnalystId, string? Technology, string? Cves, string? Description, string? Solution,
    string? Details, string? Comments, string? ImportSource, double? Cvss3BaseScore, string? Cvss3Vector,
    double? CvssBaseScore, bool? ExploitAvailable, DateTime? PatchPublicationDate, DateTime? VulnerabilityPublicationDate,
    string? LifecycleStatus, string Version);

public sealed record AgentFindingLifecycleEntry(DateTime ChangedAt, string? FromStatus, string ToStatus, string Source,
    int? UserId, string? Justification, int? DuplicateOfId);

public sealed record AgentFindingLifecycle(int FindingId, AgentPage<AgentFindingLifecycleEntry> History);

public sealed record AgentTimelineEntry(DateTime At, string Event, int? UserId, string? Text);

public sealed record AgentIncidentSummary(int Id, string Code, string Name, string? Category, string Kind, int Status,
    DateTime ReportDate, DateTime? StartDate, DateTime LastUpdate, int? AssignedToId, int? ImpactedEntityId);

public sealed record AgentIncidentDetail(int Id, string Code, string Name, string? Category, string Kind, int Status,
    DateTime ReportDate, DateTime? StartDate, DateTime CreationDate, DateTime LastUpdate, TimeSpan? Duration,
    int? AssignedToId, int? EntityId, int? ImpactedEntityId, int? ReportEntityId, string? ReportedBy, string? Description,
    string? Report, string? Impact, string? Cause, string? Solution, string? Recommendations, string? Notes,
    IReadOnlyList<int> ResponsePlanIds, AgentPage<AgentTimelineEntry> Timeline, string Version);

public sealed record AgentResponsePlanSummary(int Id, string Name, int Status, DateTime CreationDate, DateTime LastUpdate,
    bool? HasBeenTested, bool? HasBeenApproved, DateTime? LastTestDate, DateTime? ApprovalDate);

public sealed record AgentResponsePlanTask(int Id, string Name, string? Description, int Status, int ExecutionOrder,
    int Priority, string? TaskType, bool? IsOptional, TimeSpan? EstimatedDuration, int AssignedToId,
    string? SuccessCriteria, string? CompletionCriteria);

public sealed record AgentResponsePlanExecution(int Id, DateTime? ExecutionDate, int Status, TimeSpan Duration,
    bool? IsTest, bool? IsExercise, string? Trigger, string? Result, string? Notes);

public sealed record AgentResponsePlanDetail(int Id, string Name, int Status, string? Description, string? Notes,
    DateTime CreationDate, DateTime LastUpdate, bool? HasBeenTested, bool? HasBeenExercised, bool? HasBeenReviewed,
    bool? HasBeenApproved, DateTime? LastTestDate, DateTime? LastExerciseDate, DateTime? LastReviewDate,
    DateTime? ApprovalDate, AgentPage<AgentResponsePlanTask> Tasks, AgentPage<AgentResponsePlanExecution> Executions);

public sealed record AgentAssessmentSummary(int Id, string Name, DateTime Created, int? EntityId);

public sealed record AgentAssessmentRun(int Id, int AssessmentId, int EntityId, int? HostId, DateTime? RunDate, int Status,
    int ProgressPercentage, int? AnalystId, string? Comments, string Version);

public sealed record AgentAssessmentRuns(int AssessmentId, AgentPage<AgentAssessmentRun> Runs);

public sealed record AgentAssessmentAnswer(int QuestionId, string? Question, int AnswerId, string? Answer);

public sealed record AgentAssessmentRunAnswers(int AssessmentId, int RunId, AgentPage<AgentAssessmentAnswer> Answers);

public sealed record AgentAssessmentEvidence(int QuestionId, string Name, string? ContentType, int Size, DateTime UploadedAt);

public sealed record AgentAssessmentEvidenceList(int RunId, AgentCappedList<AgentAssessmentEvidence> Evidence);

public sealed record AgentEntitySummary(int Id, string Type, string? Name, string? Status, int? ParentId, DateTime Updated);

public sealed record AgentEntityProperty(string Name, string? Value);

/// <summary>
/// <see cref="WithheldProperties"/> counts the properties left out by <see cref="AgentDataPolicy.Screen"/>: not declared
/// safe, named or valued like a credential, or personal contact data.
/// </summary>
public sealed record AgentEntityDetail(int Id, string Type, string? Name, string? Status, int? ParentId, DateTime Created,
    DateTime Updated, AgentCappedList<AgentEntityProperty> Properties, int WithheldProperties, string Version);

public sealed record AgentEntityRef(int Id, string Type, string? Name);

public sealed record AgentEntityRelationships(int Id, AgentEntityRef? Parent, AgentCappedList<AgentEntityRef> Children);

public sealed record AgentHostSummary(int Id, string? HostName, string? Ip, string? Fqdn, string? Os, int Status,
    string? Source, int? TeamId, int? EntityId, DateTime RegistrationDate, DateTime? LastVerificationDate);

public sealed record AgentHostService(int Id, string Name, string? Protocol, int? Port);

public sealed record AgentSeverityCounts(int Critical, int High, int Medium, int Low, int None);

public sealed record AgentHostDetail(int Id, string? HostName, string? Ip, string? Fqdn, string? Os, string? MacAddress,
    int Status, string? Source, int? TeamId, int? EntityId, DateTime RegistrationDate, DateTime? LastVerificationDate,
    string? Comment, AgentCappedList<AgentHostService> Services, AgentSeverityCounts? OpenFindings, int? TotalFindings,
    string Version);

public sealed record AgentOverviewNode(string NodeRef, string Label, string NodeType);

public sealed record AgentOverviewHeadline(int? Displayed, string? Band, string DisplayState, int? Delta7dPoints,
    string? Delta7dDirection);

public sealed record AgentOverviewCategory(string Category, string State, int? Displayed, string? Band, double Weight);

public sealed record AgentOverviewQuality(double Coverage, double Freshness, double Quality, string? QualityBand);

public sealed record AgentRiskOverview(AgentOverviewNode Node, IReadOnlyList<AgentOverviewNode> Breadcrumb,
    DateOnly SnapshotDate, DateTime ComputedAtUtc, string Provenance, string Mode, AgentOverviewHeadline? Headline,
    IReadOnlyList<AgentOverviewCategory> Categories, AgentOverviewQuality? Quality, int ObjectCount);

public sealed record AgentOverviewChild(AgentOverviewNode Node, int? Displayed, string? Band, string State, int? Delta7d,
    double? Coverage, int ObjectCount, bool HasChildren);

public sealed record AgentOverviewChildren(AgentOverviewNode Parent, string Dimension,
    AgentCappedList<AgentOverviewChild> Children);

