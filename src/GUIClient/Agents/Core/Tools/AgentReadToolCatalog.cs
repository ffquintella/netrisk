using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Models;
using GUIClient.Tools;
using GUIClient.Tools.Hosts;
using GUIClient.Tools.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace GUIClient.Agents.ReadTools;

/// <summary>The ClientServices the read tools wrap — the same instances the GUI's own screens use.</summary>
public sealed record AgentReadServices(
    IRisksService Risks,
    IMitigationService Mitigations,
    IVulnerabilitiesService Findings,
    IIncidentsService Incidents,
    IIncidentResponsePlansService ResponsePlans,
    IAssessmentsService Assessments,
    IAssessmentEvidenceService AssessmentEvidence,
    IEntitiesService Entities,
    IHostsService Hosts);

/// <summary>
/// The M62 read tools (T351, S55 tool surface). Each is a thin wrapper over existing ClientServices calls that reach the
/// API with the current session — never a process-wide cache — with a permission rule that mirrors the API policy of the
/// endpoints it calls (T350, <see cref="AgentToolInventory"/>), bounded paging and text, and an allowlist result (T352).
/// </summary>
public static class AgentReadToolCatalog
{
    public static readonly string[] ReviewPermissions =
        { "review_insignificant", "review_low", "review_medium", "review_high", "review_veryhigh" };

    public const string ResponsePlansPermission = "incident-response-plans";

    /// <summary>The composition the GUI uses: services and module tool sources (<see cref="IAgentToolSource"/>) from DI.</summary>
    public static IReadOnlyList<IAgentTool> Create(IServiceProvider services) =>
        Create(new AgentReadServices(
                services.GetRequiredService<IRisksService>(),
                services.GetRequiredService<IMitigationService>(),
                services.GetRequiredService<IVulnerabilitiesService>(),
                services.GetRequiredService<IIncidentsService>(),
                services.GetRequiredService<IIncidentResponsePlansService>(),
                services.GetRequiredService<IAssessmentsService>(),
                services.GetRequiredService<IAssessmentEvidenceService>(),
                services.GetRequiredService<IEntitiesService>(),
                services.GetRequiredService<IHostsService>()),
            services.GetServices<IAgentToolSource>().SelectMany(source => source.Tools));

    /// <param name="moduleTools">
    /// Tools a module composes itself — the Cyber Risk Overview's (<c>Tools/RiskIndex/RiskOverviewAgentTools</c>), since
    /// only the module names the index contracts (S40 §11.5).
    /// </param>
    public static IReadOnlyList<IAgentTool> Create(AgentReadServices s, IEnumerable<IAgentTool> moduleTools) =>
        Session().Concat(Risks(s)).Concat(Findings(s)).Concat(Incidents(s)).Concat(Assessments(s))
            .Concat(Inventory(s)).Concat(moduleTools).ToList();

    private static IEnumerable<IAgentTool> Session()
    {
        yield return new AgentReadTool<AgentWhoAmI>("whoami",
            "The NetRisk user you are working as: their name and role names. Never returns a token or a key.",
            AgentToolPermission.SignedIn, AgentSchema.Object(),
            (call, _, _) =>
            {
                var user = call.User ?? throw new AgentToolException("unavailable", "Nobody is signed in to NetRisk.");
                var roles = new List<string>();
                if (!string.IsNullOrWhiteSpace(user.UserRole)) roles.Add(AgentReadMap.Title(user.UserRole)!);
                // The server issues the Admin role claim for the admin flag whatever the role is called.
                if (AdminRoleClaim.IsHeldBy(user) && !roles.Contains("Admin", StringComparer.Ordinal)) roles.Add("Admin");
                return Task.FromResult(new AgentWhoAmI(AgentReadMap.Title(user.UserName ?? user.UserAccount) ?? "", roles));
            });
    }

    private static IEnumerable<IAgentTool> Risks(AgentReadServices s)
    {
        // RequireRiskmanagement on GET /Risks, /Risks/{id}/Scoring and /Risks/{id}/Mitigation.
        var risks = AgentToolPermission.RoleOrPermission("Administrator", "riskmanagement");

        yield return new AgentReadTool<AgentPage<AgentRiskSummary>>("search_risks",
            "Search the risk register by text in the subject or reference id, optionally by status. Newest first.",
            risks,
            AgentSchema.Object(new[]
            {
                AgentSchema.Text("query", AgentResultLimits.MaxQueryLength, "Text to find in the subject or reference id."),
                AgentSchema.Text("status", 64, "Exact risk status, e.g. 'New' or 'Mitigation Planned'."),
                AgentSchema.Flag("include_closed", "Include closed risks (default false).")
            }.Concat(AgentSchema.Paging()).ToArray()),
            async (_, args, _) =>
            {
                var query = args.Text("query", AgentResultLimits.MaxQueryLength);
                var status = args.Text("status", 64);
                var (page, pageSize) = args.Paging();
                var all = await s.Risks.GetAllRisksAsync(args.Flag("include_closed", false)) ?? throw AgentToolErrors.NoAnswerError();
                var matching = all
                    .Where(r => query is null || Contains(r.Subject, query) || Contains(r.ReferenceId, query))
                    .Where(r => status is null || string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.LastUpdate).ThenBy(r => r.Id);
                return AgentPage<AgentRiskSummary>.Of(matching, page, pageSize, AgentReadMap.RiskSummary);
            });

        yield return new AgentReadTool<AgentRiskDetail>("get_risk",
            "One risk: subject, status, owner, assessment and notes, scenario, links and current score.",
            risks, AgentSchema.Object(AgentSchema.Id("risk_id", "The risk id.")),
            async (_, args, _) =>
            {
                var id = args.Id("risk_id");
                // IRisksService has no single-risk read; the register list (closed included) is the call the GUI uses.
                var all = await s.Risks.GetAllRisksAsync(includeClosed: true) ?? throw AgentToolErrors.NoAnswerError();
                var risk = all.FirstOrDefault(r => r.Id == id) ?? throw AgentToolErrors.NotFoundError("risk");
                DAL.Entities.RiskScoring? scoring;
                try
                {
                    scoring = await s.Risks.GetRiskScoringAsync(id);
                }
                catch (Exception ex) when (AgentToolErrors.From(ex).Status == AgentToolErrors.NotFound)
                {
                    scoring = null; // a risk without a score; any other failure fails the whole read
                }
                return AgentReadMap.RiskDetail(risk, scoring);
            });

        yield return new AgentReadTool<AgentRiskMitigation>("get_risk_mitigation",
            "The mitigation plan of one risk: strategy, effort, cost, owner, solution and progress.",
            risks, AgentSchema.Object(AgentSchema.Id("risk_id", "The risk id.")),
            async (_, args, _) =>
            {
                var mitigation = await s.Mitigations.GetByRiskIdAsync(args.Id("risk_id"))
                                 ?? throw AgentToolErrors.NotFoundError("mitigation for a risk");
                return AgentReadMap.Mitigation(mitigation);
            });

        // RequireMgmtReviewAccess on GET /Risks/{id}/MgmtReviews: one of the review permissions, no role.
        yield return new AgentReadTool<AgentRiskReviews>("list_risk_reviews",
            "The management reviews of one risk, newest first: decision, reviewer, next step and next review date.",
            AgentToolPermission.AnyOf(ReviewPermissions),
            AgentSchema.Object(AgentSchema.Id("risk_id", "The risk id."), NestedPage("page", "reviews"), ItemsPerPage),
            (_, args, _) =>
            {
                var id = args.Id("risk_id");
                var page = NestedPageOf(args, "page");
                var reviews = s.Risks.GetRiskMgmtReviews(id) ?? throw AgentToolErrors.NoAnswerError();
                return Task.FromResult(new AgentRiskReviews(id, AgentPage<AgentRiskReview>.Of(
                    reviews.OrderByDescending(r => r.SubmissionDate), page, ItemsPerPageOf(args),
                    AgentReadMap.Review)));
            });
    }

    private static IEnumerable<IAgentTool> Findings(AgentReadServices s)
    {
        var findings = AgentToolPermission.Granted("vulnerabilities");

        yield return new AgentReadTool<AgentPage<AgentFindingSummary>>("search_findings",
            "Search vulnerabilities and findings with the findings screen's filter language (e.g. 'title@=*openssl', " +
            "'severity==Critical'). An empty filter lists everything, one page at a time.",
            findings,
            AgentSchema.Object(new[]
            {
                AgentSchema.Text("filter", AgentResultLimits.MaxFilterLength,
                    "Filter expression as typed in the findings screen; the server validates it.")
            }.Concat(AgentSchema.Paging()).ToArray()),
            async (_, args, _) =>
            {
                var filter = args.Text("filter", AgentResultLimits.MaxFilterLength) ?? "";
                var (page, pageSize) = args.Paging();
                var answer = await s.Findings.GetFilteredAsync(pageSize, page, filter) ?? throw AgentToolErrors.NoAnswerError();
                var items = (answer.Item1 ?? throw AgentToolErrors.NoAnswerError())
                    .Take(pageSize).Select(AgentReadMap.FindingSummary).ToList();
                var total = Math.Max(answer.Item2, items.Count);
                return new AgentPage<AgentFindingSummary>(items, total, page, pageSize, (long)page * pageSize < total);
            });

        yield return new AgentReadTool<AgentFindingDetail>("get_finding",
            "One vulnerability or finding: description, solution, scores, detection dates, host and status.",
            findings, AgentSchema.Object(AgentSchema.Id("finding_id", "The finding (vulnerability) id.")),
            async (_, args, _) =>
            {
                var finding = await s.Findings.GetOneAsync(args.Id("finding_id")) ?? throw AgentToolErrors.NoAnswerError();
                return AgentReadMap.FindingDetail(finding);
            });

        yield return new AgentReadTool<AgentFindingLifecycle>("get_finding_lifecycle",
            "The triage lifecycle history of one finding, newest first: each status change, its source and justification.",
            findings,
            AgentSchema.Object(AgentSchema.Id("finding_id", "The finding (vulnerability) id."), NestedPage("page", "changes"), ItemsPerPage),
            async (_, args, _) =>
            {
                var id = args.Id("finding_id");
                var page = NestedPageOf(args, "page");
                var history = await s.Findings.GetStatusHistoryAsync(id) ?? throw AgentToolErrors.NoAnswerError();
                return new AgentFindingLifecycle(id, AgentPage<AgentFindingLifecycleEntry>.Of(
                    history.OrderByDescending(h => h.ChangedAt), page, ItemsPerPageOf(args),
                    AgentReadMap.LifecycleEntry));
            });
    }

    private static IEnumerable<IAgentTool> Incidents(AgentReadServices s)
    {
        var incidents = AgentToolPermission.Granted("incident_management");
        var plans = AgentToolPermission.Granted(ResponsePlansPermission);

        yield return new AgentReadTool<AgentPage<AgentIncidentSummary>>("search_incidents",
            "Search incidents by text in the name, code or category, optionally by status. Most recently reported first.",
            incidents,
            AgentSchema.Object(new[]
            {
                AgentSchema.Text("query", AgentResultLimits.MaxQueryLength, "Text to find in the name, code or category."),
                AgentSchema.Int("status", 0, 1000, "Exact incident status id.")
            }.Concat(AgentSchema.Paging()).ToArray()),
            async (_, args, _) =>
            {
                var query = args.Text("query", AgentResultLimits.MaxQueryLength);
                var status = args.OptionalInt("status", 0, 1000);
                var (page, pageSize) = args.Paging();
                var all = await s.Incidents.GetAllAsync() ?? throw AgentToolErrors.NoAnswerError();
                var matching = all
                    .Where(i => query is null || Contains(i.Name, query) || Contains(i.Category, query)
                                || Contains(AgentReadMap.IncidentCode(i), query))
                    .Where(i => status is null || i.Status == status)
                    .OrderByDescending(i => i.ReportDate).ThenBy(i => i.Id);
                return AgentPage<AgentIncidentSummary>.Of(matching, page, pageSize, AgentReadMap.IncidentSummary);
            });

        yield return new AgentReadTool<AgentIncidentDetail>("get_incident",
            "One incident with its description, impact, cause, solution, linked response plans and a dated timeline.",
            incidents,
            AgentSchema.Object(AgentSchema.Id("incident_id", "The incident id."), NestedPage("timeline_page", "timeline entries"), ItemsPerPage),
            async (_, args, _) =>
            {
                var id = args.Id("incident_id");
                var timelinePage = NestedPageOf(args, "timeline_page");
                // IIncidentsService has no single-incident read; the list is the call the GUI uses.
                var all = await s.Incidents.GetAllAsync() ?? throw AgentToolErrors.NoAnswerError();
                var incident = all.FirstOrDefault(i => i.Id == id) ?? throw AgentToolErrors.NotFoundError("incident");
                var planIds = await s.Incidents.GetIncidentResponsPlanIdsByIdAsync(id) ?? throw AgentToolErrors.NoAnswerError();
                return AgentReadMap.IncidentDetail(incident, planIds, timelinePage, ItemsPerPageOf(args));
            });

        yield return new AgentReadTool<AgentPage<AgentResponsePlanSummary>>("search_response_plans",
            "Search incident response plans by text in the name. Most recently updated first.",
            plans,
            AgentSchema.Object(new[]
            {
                AgentSchema.Text("query", AgentResultLimits.MaxQueryLength, "Text to find in the plan name.")
            }.Concat(AgentSchema.Paging()).ToArray()),
            async (_, args, _) =>
            {
                var query = args.Text("query", AgentResultLimits.MaxQueryLength);
                var (page, pageSize) = args.Paging();
                var all = await s.ResponsePlans.GetAllAsync() ?? throw AgentToolErrors.NoAnswerError();
                var matching = all.Where(p => query is null || Contains(p.Name, query))
                    .OrderByDescending(p => p.LastUpdate).ThenBy(p => p.Id);
                return AgentPage<AgentResponsePlanSummary>.Of(matching, page, pageSize, AgentReadMap.PlanSummary);
            });

        yield return new AgentReadTool<AgentResponsePlanDetail>("get_response_plan",
            "One incident response plan with a page of its tasks (in execution order) and a page of its executions " +
            "(newest first); page through them with task_page and execution_page.",
            plans,
            AgentSchema.Object(AgentSchema.Id("plan_id", "The response plan id."), NestedPage("task_page", "tasks"),
                NestedPage("execution_page", "executions"), ItemsPerPage),
            async (_, args, _) =>
            {
                var id = args.Id("plan_id");
                var taskPage = NestedPageOf(args, "task_page");
                var executionPage = NestedPageOf(args, "execution_page");
                var plan = await s.ResponsePlans.GetByIdAsync(id, includeTasks: true) ?? throw AgentToolErrors.NoAnswerError();
                var executions = await s.ResponsePlans.GetExecutionsByPlanIdAsync(id) ?? throw AgentToolErrors.NoAnswerError();
                return AgentReadMap.PlanDetail(plan, executions, taskPage, executionPage, ItemsPerPageOf(args));
            });
    }

    private static IEnumerable<IAgentTool> Assessments(AgentReadServices s)
    {
        // RequireAssessmentAccess (AssessmentsController, AssessmentRunEvidenceController): the claim, no role.
        var assessments = AgentToolPermission.Claim("assessments");

        yield return new AgentReadTool<AgentPage<AgentAssessmentSummary>>("list_assessments",
            "List assessments (questionnaires), optionally by text in the name.",
            assessments,
            AgentSchema.Object(new[]
            {
                AgentSchema.Text("query", AgentResultLimits.MaxQueryLength, "Text to find in the assessment name.")
            }.Concat(AgentSchema.Paging()).ToArray()),
            async (_, args, _) =>
            {
                var query = args.Text("query", AgentResultLimits.MaxQueryLength);
                var (page, pageSize) = args.Paging();
                var all = await s.Assessments.GetAssessmentsAsync() ?? throw AgentToolErrors.NoAnswerError();
                var matching = all.Where(a => query is null || Contains(a.Name, query)).OrderBy(a => a.Id);
                return AgentPage<AgentAssessmentSummary>.Of(matching, page, pageSize, AgentReadMap.Assessment);
            });

        yield return new AgentReadTool<AgentAssessmentRuns>("list_assessment_runs",
            "The runs of one assessment, newest first: entity or host assessed, status and progress.",
            assessments,
            AgentSchema.Object(AgentSchema.Id("assessment_id", "The assessment id."), NestedPage("page", "runs"), ItemsPerPage),
            (_, args, _) =>
            {
                var id = args.Id("assessment_id");
                var page = NestedPageOf(args, "page");
                var runs = s.Assessments.GetAssessmentRuns(id) ?? throw AgentToolErrors.NoAnswerError();
                return Task.FromResult(new AgentAssessmentRuns(id, AgentPage<AgentAssessmentRun>.Of(
                    runs.OrderByDescending(r => r.RunDate), page, ItemsPerPageOf(args), AgentReadMap.AssessmentRun)));
            });

        yield return new AgentReadTool<AgentAssessmentRunAnswers>("get_assessment_run_answers",
            "The answers given in one assessment run: each question and the answer chosen, a page at a time.",
            assessments,
            AgentSchema.Object(AgentSchema.Id("assessment_id", "The assessment id."), AgentSchema.Id("run_id", "The run id."),
                NestedPage("page", "answers"), ItemsPerPage),
            (_, args, _) =>
            {
                var assessmentId = args.Id("assessment_id");
                var runId = args.Id("run_id");
                var page = NestedPageOf(args, "page");
                var answers = s.Assessments.GetAssessmentRunAnsers(assessmentId, runId) ?? throw AgentToolErrors.NoAnswerError();
                return Task.FromResult(new AgentAssessmentRunAnswers(assessmentId, runId, AgentPage<AgentAssessmentAnswer>.Of(
                    answers.OrderBy(a => a.QuestionId).ThenBy(a => a.Id), page, ItemsPerPageOf(args),
                    AgentReadMap.RunAnswer)));
            });

        yield return new AgentReadTool<AgentAssessmentEvidenceList>("list_assessment_evidence",
            "The evidence files attached to the answers of one assessment run: name, type, size and date (not the content).",
            assessments, AgentSchema.Object(AgentSchema.Id("run_id", "The run id.")),
            async (_, args, _) =>
            {
                var runId = args.Id("run_id");
                var evidence = await s.AssessmentEvidence.GetRunEvidenceAsync(runId) ?? throw AgentToolErrors.NoAnswerError();
                return new AgentAssessmentEvidenceList(runId, AgentCappedList<AgentAssessmentEvidence>.Of(
                    evidence.OrderBy(e => e.QuestionId).ThenBy(e => e.Timestamp), AgentReadMap.Evidence));
            });
    }

    private static IEnumerable<IAgentTool> Inventory(AgentReadServices s)
    {
        // EntitiesController reads need only a valid user; the tools keep the GUI's stricter module rule.
        var entities = AgentToolPermission.Module(AvaliableViews.Entities);
        var hosts = AgentToolPermission.Granted("hosts");

        yield return new AgentReadTool<AgentPage<AgentEntitySummary>>("search_entities",
            "Search entities (organisation units, applications, processes, people…) by name, optionally by type.",
            entities,
            AgentSchema.Object(new[]
            {
                AgentSchema.Text("query", AgentResultLimits.MaxQueryLength, "Text to find in the entity name."),
                AgentSchema.Text("type", 64, "Entity type (definition name), e.g. 'application'.")
            }.Concat(AgentSchema.Paging()).ToArray()),
            async (_, args, _) =>
            {
                var query = args.Text("query", AgentResultLimits.MaxQueryLength);
                var type = args.Text("type", 64);
                var (page, pageSize) = args.Paging();
                var all = await s.Entities.GetAllFromServerAsync() ?? throw AgentToolErrors.NoAnswerError();
                var matching = all
                    .Where(e => type is null || string.Equals(e.DefinitionName, type, StringComparison.OrdinalIgnoreCase))
                    .Where(e => query is null || Contains(AgentReadMap.EntityName(e), query))
                    .OrderBy(e => e.Id);
                return AgentPage<AgentEntitySummary>.Of(matching, page, pageSize, AgentReadMap.EntitySummary);
            });

        yield return new AgentReadTool<AgentEntityDetail>("get_entity",
            "One entity with its properties. Only properties reviewed as safe are returned; credential-like and personal " +
            "contact properties are withheld and counted.",
            entities, AgentSchema.Object(AgentSchema.Id("entity_id", "The entity id.")),
            async (call, args, _) =>
            {
                var id = args.Id("entity_id");
                // Always from the API with this session; IEntitiesService.GetEntity reads a process-wide cache.
                var all = await s.Entities.GetAllFromServerAsync() ?? throw AgentToolErrors.NoAnswerError();
                var entity = all.FirstOrDefault(e => e.Id == id) ?? throw AgentToolErrors.NotFoundError("entity");
                return AgentReadMap.EntityDetail(entity, call.Withheld);
            });

        yield return new AgentReadTool<AgentEntityRelationships>("get_entity_relationships",
            "The parent and the direct children of one entity in the entity map.",
            entities, AgentSchema.Object(AgentSchema.Id("entity_id", "The entity id.")),
            async (_, args, _) =>
            {
                var id = args.Id("entity_id");
                var all = await s.Entities.GetAllFromServerAsync() ?? throw AgentToolErrors.NoAnswerError();
                var entity = all.FirstOrDefault(e => e.Id == id) ?? throw AgentToolErrors.NotFoundError("entity");
                var parent = entity.Parent is { } parentId ? all.FirstOrDefault(e => e.Id == parentId) : null;
                return new AgentEntityRelationships(id, parent is null ? null : AgentReadMap.EntityRef(parent),
                    AgentCappedList<AgentEntityRef>.Of(all.Where(e => e.Parent == id).OrderBy(e => e.Id),
                        AgentReadMap.EntityRef));
            });

        yield return new AgentReadTool<AgentPage<AgentHostSummary>>("search_hosts",
            "Search hosts (devices) by host name, optionally by status, sorted by host name.",
            hosts,
            AgentSchema.Object(new[]
            {
                AgentSchema.Text("query", AgentResultLimits.MaxQueryLength, "Text to find in the host name."),
                AgentSchema.Int("status", 0, 1000, "Exact host status id.")
            }.Concat(AgentSchema.Paging()).ToArray()),
            async (_, args, _) =>
            {
                var filter = HostsFilterComposer.Compose(args.Text("query", AgentResultLimits.MaxQueryLength),
                    args.OptionalInt("status", 0, 1000), null, null, null);
                var (page, pageSize) = args.Paging();
                var (items, total) = await s.Hosts.GetFilteredAsync(pageSize, page, filter, IHostsService.DefaultSort);
                var mapped = (items ?? throw AgentToolErrors.NoAnswerError()).Take(pageSize)
                    .Select(AgentReadMap.HostSummary).ToList();
                total = Math.Max(total, mapped.Count);
                return new AgentPage<AgentHostSummary>(mapped, total, page, pageSize, (long)page * pageSize < total);
            });

        yield return new AgentReadTool<AgentHostDetail>("get_host",
            "One host with its network services and a count of its open findings by severity.",
            hosts, AgentSchema.Object(AgentSchema.Id("host_id", "The host id.")),
            async (_, args, _) =>
            {
                var id = args.Id("host_id");
                // The filtered list endpoint, by id: always the API with this session. IHostsService.GetOne answers from a
                // process-wide cache that outlives a session.
                var (items, _) = await s.Hosts.GetFilteredAsync(1, 1, RecordNavigation.HostFilter(id), null);
                var host = items?.FirstOrDefault(h => h.Id == id) ?? throw AgentToolErrors.NotFoundError("host");
                var services = await s.Hosts.GetAllHostServiceAsync(id) ?? throw AgentToolErrors.NoAnswerError();
                var summary = await s.Hosts.GetVulnerabilitySummaryAsync(id);
                return AgentReadMap.HostDetail(host, services, summary);
            });
    }

    private static (string, System.Text.Json.Nodes.JsonObject, bool) NestedPage(string name, string what) =>
        AgentSchema.Int(name, 1, AgentResultLimits.MaxPage,
            $"Page of {what}, from 1 (default 1); 'items_per_page' items per page.");

    private static readonly (string, System.Text.Json.Nodes.JsonObject, bool) ItemsPerPage =
        AgentSchema.Int("items_per_page", 1, AgentResultLimits.NestedPageSize,
            $"Items per page of the lists inside the item (default and maximum {AgentResultLimits.NestedPageSize}); " +
            "lower it if a page is too large.");

    private static int NestedPageOf(AgentArgs args, string name) =>
        args.OptionalInt(name, 1, AgentResultLimits.MaxPage) ?? 1;

    private static int ItemsPerPageOf(AgentArgs args) =>
        args.OptionalInt("items_per_page", 1, AgentResultLimits.NestedPageSize) ?? AgentResultLimits.NestedPageSize;

    private static bool Contains(string? value, string query) =>
        value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
