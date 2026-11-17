using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using GUIClient.Agents.ReadTools;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Entities;

namespace GUIClient.Agents.EditTools;

/// <summary>The ClientServices the edit tools wrap — the same instances the GUI's screens use.</summary>
public sealed record AgentEditServices(
    IRisksService Risks,
    IVulnerabilitiesService Findings,
    ITeamsService Teams,
    IIncidentsService Incidents,
    IAssessmentsService Assessments,
    IEntitiesService Entities,
    IHostsService Hosts);

/// <summary>How lifecycle states are named to an agent, and the only ones it may set (T354).</summary>
public static class AgentEditNames
{
    /// <summary>
    /// The open-work states. Every other one carries a decision the agent may not take: FalsePositive and OutOfScope
    /// suppress, Duplicate merges, RiskAccepted needs an authorized acceptance, Mitigated closes the finding.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, FindingStatus> SettableLifecycle =
        new Dictionary<string, FindingStatus>(StringComparer.Ordinal)
        {
            ["active"] = FindingStatus.Active,
            ["verified"] = FindingStatus.Verified
        };

    public static string Lifecycle(FindingStatus status) =>
        System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString());
}

/// <summary>
/// The write rules of the edit tools (T354), each the mirror of every endpoint the tool calls, including the read it
/// makes first; and, because every policy the API builds carries <c>ThirdLineReadOnlyRequirement</c>, never the third line.
/// </summary>
public static class AgentWritePermissions
{
    // GET /Risks (RequireRiskmanagement) + GET /Risks/Exists and PUT /Risks/{id} (RequireSubmitRisk: the claim alone).
    public static AgentToolPermission UpdateRisk { get; } = AgentToolPermission.RoleOrPermission("Administrator", "riskmanagement")
        .And(AgentToolPermission.Claim("submit_risks")).And(AgentToolPermission.NotThirdLine);

    // Class [PermissionAuthorize("vulnerabilities")] + PUT {id}/status [PermissionAuthorize("vulnerabilities_update")].
    public static AgentToolPermission FindingStatus { get; } = AgentToolPermission.Granted("vulnerabilities")
        .And(AgentToolPermission.Granted("vulnerabilities_update")).And(AgentToolPermission.NotThirdLine);

    // PUT /Vulnerabilities/{id} and POST {id}/RisksAssociate: vulnerabilities + vulnerabilities_create (GET /Teams: any user).
    public static AgentToolPermission FindingAssignment { get; } = AgentToolPermission.Granted("vulnerabilities")
        .And(AgentToolPermission.Granted("vulnerabilities_create")).And(AgentToolPermission.NotThirdLine);

    // POST /Vulnerabilities/{id}/Actions: the class rule only.
    public static AgentToolPermission FindingNote { get; } =
        AgentToolPermission.Granted("vulnerabilities").And(AgentToolPermission.NotThirdLine);

    // T354: GET /Risks/Categories and /Sources (valid user), GET /Risks/Exists and POST /Risks, POST /Risks/{id}/Scoring
    // (RequireSubmitRisk), GET /Risks/ScoreValue-1-1 and GET /Risks (RequireRiskmanagement): the update_risk rule exactly.
    public static AgentToolPermission CreateRisk { get; } = UpdateRisk;

    // Class [PermissionAuthorize("incident_management")] (GET /Incidents also needs FaceID when on, invisible here).
    public static AgentToolPermission Incident { get; } =
        AgentToolPermission.Granted("incident_management").And(AgentToolPermission.NotThirdLine);

    // T354: GET /Incidents/NextSequence, POST /Incidents and POST /Incidents/{id}/Timeline: the class rule, as PUT has.
    public static AgentToolPermission CreateIncident { get; } = Incident;
    public static AgentToolPermission IncidentTimeline { get; } = Incident;

    // RequireAssessmentAccess on every AssessmentsController action: the claim, no role.
    public static AgentToolPermission Assessment { get; } =
        AgentToolPermission.Claim("assessments").And(AgentToolPermission.NotThirdLine);

    // PUT /Entities/{id}: [PermissionAuthorize(entities_manage)]; the read needs only a valid user.
    public static AgentToolPermission Entity { get; } =
        AgentToolPermission.Granted(EntityPermissions.Manage).And(AgentToolPermission.NotThirdLine);

    // Class [PermissionAuthorize("hosts")] + PUT /Hosts/{id} [PermissionAuthorize("hosts_create")].
    public static AgentToolPermission Host { get; } = AgentToolPermission.Granted("hosts")
        .And(AgentToolPermission.Granted("hosts_create")).And(AgentToolPermission.NotThirdLine);
}

/// <summary>
/// The M63 edit tools (T354, S55 tool surface): single-item changes through existing ClientServices calls, each reading the
/// item fresh, refusing a stale version, validating as the GUI does, and preparing an exact diff for the confirmation gate.
/// What each may and may not set is listed in the spec's "Implementation notes (M63)".
/// </summary>
public static class AgentEditToolCatalog
{
    /// <summary>The entity properties an agent may write: plain descriptive text, never risk context, links or flags.</summary>
    public static readonly IReadOnlyDictionary<string, int> EditableEntityProperties =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["name"] = AgentEditLimits.TitleLength,
            ["description"] = AgentEditLimits.TextLength,
            ["objective"] = AgentEditLimits.TextLength,
            ["technology"] = AgentEditLimits.TitleLength
        };

    /// <summary>Entity definitions an agent may not edit at all: a person's record is personal data (LGPD).</summary>
    public static readonly IReadOnlySet<string> ProtectedDefinitions = new HashSet<string>(StringComparer.Ordinal) { "person" };

    public static IReadOnlyList<IAgentTool> Create(IServiceProvider services) =>
        Create(new AgentEditServices(
            services.GetRequiredService<IRisksService>(),
            services.GetRequiredService<IVulnerabilitiesService>(),
            services.GetRequiredService<ITeamsService>(),
            services.GetRequiredService<IIncidentsService>(),
            services.GetRequiredService<IAssessmentsService>(),
            services.GetRequiredService<IEntitiesService>(),
            services.GetRequiredService<IHostsService>()));

    public static IReadOnlyList<IAgentTool> Create(AgentEditServices s) =>
        Risks(s).Concat(Findings(s)).Concat(Incidents(s)).Concat(Assessments(s)).Concat(Inventory(s)).ToList();

    private static readonly (string, System.Text.Json.Nodes.JsonObject, bool) VersionArgument =
        (AgentEditArgs.VersionArgument, new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "string", ["maxLength"] = AgentEditLimits.VersionLength,
            ["description"] = "The item's 'version' from its read tool. A stale version is refused: read again."
        }, true);

    private static (string, System.Text.Json.Nodes.JsonObject, bool) Text(string name, int max, string description) =>
        AgentSchema.Text(name, max, description + " " + StoredAsUserText);

    /// <summary>
    /// What is true for every edit (review M63 #9): the text is stored as the user's own; the server logs the request as made
    /// through an agent. The field itself carries no marker.
    /// </summary>
    public const string StoredAsUserText =
        "Stored as the signed-in user's text; NetRisk's server log records that the request came through an agent " +
        "(the text itself carries no marker).";

    // ---- Risks -----------------------------------------------------------------------------------------------------

    private static IEnumerable<IAgentTool> Risks(AgentEditServices s)
    {
        yield return new AgentEditTool("update_risk",
            "Change the descriptive text of one risk: subject, notes and the four scenario fields. Never its status, owner, " +
            "category, score, review or closure — those stay with the user.",
            AgentWritePermissions.UpdateRisk,
            AgentSchema.Object(AgentSchema.Id("risk_id", "The risk id."), VersionArgument,
                Text("subject", AgentEditLimits.TitleLength, "New subject; must stay unique."),
                Text("notes", AgentEditLimits.TextLength, "New notes."),
                Text("scenario_cause", AgentEditLimits.TextLength, "New scenario cause."),
                Text("scenario_vulnerability", AgentEditLimits.TextLength, "New scenario vulnerability."),
                Text("scenario_central_event", AgentEditLimits.TextLength, "New scenario central event."),
                Text("scenario_consequences", AgentEditLimits.TextLength, "New scenario consequences.")),
            async (call, args, ct) =>
            {
                var id = args.Id("risk_id");
                var version = args.Version();
                var subject = args.Text("subject", AgentEditLimits.TitleLength);
                var updates = new (string Field, string? Value, Func<Risk, string?> Get, Action<Risk, string?> Set)[]
                {
                    ("subject", subject, r => r.Subject, (r, v) => r.Subject = v!),
                    ("notes", args.Text("notes", AgentEditLimits.TextLength, allowEmpty: true), r => r.Notes, (r, v) => r.Notes = v ?? ""),
                    ("scenario_cause", args.Text("scenario_cause", AgentEditLimits.TextLength, allowEmpty: true),
                        r => r.ScenarioCause, (r, v) => r.ScenarioCause = Blank(v)),
                    ("scenario_vulnerability", args.Text("scenario_vulnerability", AgentEditLimits.TextLength, allowEmpty: true),
                        r => r.ScenarioVulnerability, (r, v) => r.ScenarioVulnerability = Blank(v)),
                    ("scenario_central_event", args.Text("scenario_central_event", AgentEditLimits.TextLength, allowEmpty: true),
                        r => r.ScenarioCentralEvent, (r, v) => r.ScenarioCentralEvent = Blank(v)),
                    ("scenario_consequences", args.Text("scenario_consequences", AgentEditLimits.TextLength, allowEmpty: true),
                        r => r.ScenarioConsequences, (r, v) => r.ScenarioConsequences = Blank(v))
                };
                if (updates.All(u => u.Value is null)) throw NothingToChange();

                var risk = await FreshRisk(s, id);
                // T362: a withheld item is refused before anything else is compared or validated.
                await call.Classification.EnsureVisibleAsync(AgentItemLinks.ForRisk(risk), edit: true, ct);
                if (AgentVersions.Risk(risk) != version) throw AgentEditArgs.Stale("get_risk");

                // EditRiskViewModel's rules: a subject is required (Text refuses blank) and unique, compared trimmed.
                if (subject is not null && !string.Equals(risk.Subject?.TrimEnd(), subject.TrimEnd(), StringComparison.Ordinal)
                    && await Task.Run(() => s.Risks.RiskSubjectExists(subject)))
                    throw new AgentToolException(AgentToolErrors.Invalid,
                        "Another risk already has this subject. Nothing was written.");

                var changes = Diff(risk, updates.Where(u => u.Value is not null).Select(u => (u.Field, u.Value, u.Get)));
                return new AgentEditPlan(new AgentItemRef(AgentItemKinds.Risk, id), Label(risk.Subject), changes, "updated",
                    async _ =>
                    {
                        // Review M63 #2: the user may have taken a while to approve. Write onto a copy read now, and only
                        // if nobody changed the risk since the plan was made — the PUT carries the whole record.
                        var fresh = await FreshRisk(s, id);
                        if (AgentVersions.Risk(fresh) != version) throw AgentEditArgs.ChangedSincePrepared("get_risk");
                        foreach (var u in updates.Where(u => u.Value is not null)) u.Set(fresh, u.Value);
                        await Task.Run(() => s.Risks.SaveRisk(fresh));
                        return await TryVersion(async () => AgentVersions.Risk(await FreshRisk(s, id)));
                    }, version);
            });

        yield return new AgentEditTool("create_risk",
            "Create one new risk with a subject, notes and the four scenario fields, filed under a category and a source " +
            "from NetRisk's own lists (ids from list_risk_categories_and_sources). The signed-in user becomes its owner and " +
            "submitter; it starts as New with the screen's default score (likelihood 1, impact 1) and no manager, entity, " +
            "catalog, mitigation or review: those stay with the user. NetRisk sends its risk-created notification to its " +
            "subscribers. The user approves every new risk on screen first.",
            AgentWritePermissions.CreateRisk,
            AgentSchema.Object(
                Text("subject", AgentEditLimits.TitleLength, "The subject; required and unique."),
                Text("notes", AgentEditLimits.TextLength, "Notes."),
                Text("scenario_cause", AgentEditLimits.TextLength, "Scenario cause."),
                Text("scenario_vulnerability", AgentEditLimits.TextLength, "Scenario vulnerability."),
                Text("scenario_central_event", AgentEditLimits.TextLength, "Scenario central event."),
                Text("scenario_consequences", AgentEditLimits.TextLength, "Scenario consequences."),
                AgentSchema.Id("category_id", "A risk category from NetRisk's list; the user sees it by name before approving."),
                AgentSchema.Id("source_id", "A risk source from NetRisk's list; the user sees it by name before approving.")),
            async (call, args, ct) =>
            {
                var userId = call.User?.UserId ?? throw new AgentToolException("unavailable", "Nobody is signed in to NetRisk.");
                var subject = args.Text("subject", AgentEditLimits.TitleLength, required: true)!;
                var texts = new (string Field, string? Value)[]
                {
                    ("notes", args.Text("notes", AgentEditLimits.TextLength, allowEmpty: true)),
                    ("scenario_cause", args.Text("scenario_cause", AgentEditLimits.TextLength, allowEmpty: true)),
                    ("scenario_vulnerability", args.Text("scenario_vulnerability", AgentEditLimits.TextLength, allowEmpty: true)),
                    ("scenario_central_event", args.Text("scenario_central_event", AgentEditLimits.TextLength, allowEmpty: true)),
                    ("scenario_consequences", args.Text("scenario_consequences", AgentEditLimits.TextLength, allowEmpty: true))
                };
                var categoryId = args.Id("category_id");
                var sourceId = args.Id("source_id");

                // EditRiskViewModel's pickers are these lists; an id that is not on them is refused the same way.
                var category = (await s.Risks.GetRiskCategoriesAsync() ?? throw AgentToolErrors.NoAnswerError())
                               .FirstOrDefault(c => c.Value == categoryId) ?? throw AgentToolErrors.NotFoundError("risk category");
                var sources = s.Risks.GetRiskSourcesAsync() is { } pending ? await pending : null;
                var source = (sources ?? throw AgentToolErrors.NoAnswerError())
                             .FirstOrDefault(x => x.Value == sourceId) ?? throw AgentToolErrors.NotFoundError("risk source");
                // EditRiskViewModel's rules: a subject is required (Text refuses blank) and unique.
                if (await Task.Run(() => s.Risks.RiskSubjectExists(subject))) throw SubjectTaken();
                // The screen's default for a new risk: likelihood 1, impact 1, classic scoring. A score NetRisk cannot
                // compute is refused here rather than saved as 0 (the screen would save 0).
                var score = await Task.Run(() => s.Risks.GetRiskScore(1, 1));
                if (!(score > 0)) throw AgentToolErrors.NoAnswerError();

                var changes = new List<AgentFieldChange> { new("subject", null, subject) };
                changes.AddRange(texts.Where(t => !string.IsNullOrEmpty(t.Value)).Select(t => new AgentFieldChange(t.Field, null, t.Value)));
                changes.Add(new AgentFieldChange("category", null, Label(category.Name) + " (#" + categoryId + ")"));
                changes.Add(new AgentFieldChange("source", null, Label(source.Name) + " (#" + sourceId + ")"));
                changes.Add(new AgentFieldChange("status", null, "New"));
                changes.Add(new AgentFieldChange("owner", null, "#" + userId + " (" + AgentSessionUserLabel(call) + ")"));
                changes.Add(new AgentFieldChange("score", null,
                    "classic: likelihood 1 x impact 1 = " + score.ToString("0.##", CultureInfo.InvariantCulture)));
                string? Value(string field) => texts.First(t => t.Field == field).Value;

                return AgentEditPlan.NewItem(AgentItemKinds.Risk, Label(subject), changes, async cancellation =>
                {
                    // Review M63 #2 for a creation: still unique now, not only when the plan was made.
                    if (await Task.Run(() => s.Risks.RiskSubjectExists(subject)))
                        throw new AgentToolException(AgentToolErrors.Stale,
                            "Another risk with this subject was created after your change was prepared. Nothing was written.");
                    var now = DateTime.Now;
                    var created = await Task.Run(() => s.Risks.CreateRisk(new Risk
                    {
                        Subject = subject, Notes = Value("notes") ?? "", ScenarioCause = Blank(Value("scenario_cause")),
                        ScenarioVulnerability = Blank(Value("scenario_vulnerability")),
                        ScenarioCentralEvent = Blank(Value("scenario_central_event")),
                        ScenarioConsequences = Blank(Value("scenario_consequences")),
                        Category = categoryId, Source = sourceId, Status = "New", Owner = userId, SubmittedBy = userId,
                        SubmissionDate = now, LastUpdate = now,
                        Assessment = "", RiskCatalogMapping = "", ThreatCatalogMapping = "", ReferenceId = ""
                    })) ?? throw AgentToolErrors.NoAnswerError();

                    var notWritten = new List<string>();
                    try
                    {
                        // As the screen does, right after the risk: its own row, so it can fail on its own.
                        await Task.Run(() => s.Risks.CreateRiskScoring(new RiskScoring
                        {
                            Id = created.Id, ScoringMethod = 1, ClassicLikelihood = 1, ClassicImpact = 1, CalculatedRisk = score
                        }));
                    }
                    catch (Exception)
                    {
                        notWritten.Add("score");
                    }
                    return await CreatedAsync(call, created.Id, () => FreshRisk(s, created.Id), AgentItemLinks.ForRisk,
                        AgentVersions.Risk, notWritten, cancellation);
                }, RiskCreationSideEffectsKey);
            }, createsItems: true);
    }

    private static AgentToolException SubjectTaken() =>
        new(AgentToolErrors.Invalid, "Another risk already has this subject. Nothing was written.");

    /// <summary>
    /// T354, T362: what a creation made, read back fresh. The server may file a new item under the user's only entity
    /// (Track 2 scope); one the policy withholds — or one that cannot be read back to tell — is reported without its id.
    /// </summary>
    private static async Task<AgentCreatedItem> CreatedAsync<T>(AgentToolCall call, int id, Func<Task<T>> fresh,
        Func<T, AgentItemLinks> links, Func<T, string> version, IReadOnlyList<string> notWritten,
        System.Threading.CancellationToken ct)
    {
        T item;
        try
        {
            item = await fresh();
        }
        catch (Exception)
        {
            return new AgentCreatedItem(id, null, Withheld: call.Classification.IsActive, notWritten);
        }
        var withheld = await call.Classification.IsExcludedAsync(links(item), ct);
        return new AgentCreatedItem(id, withheld ? null : version(item), withheld, notWritten);
    }

    private static async Task<Risk> FreshRisk(AgentEditServices s, int id)
    {
        // IRisksService has no single-risk read; the register list (closed included) is what EditRiskViewModel edits.
        var all = await s.Risks.GetAllRisksAsync(includeClosed: true) ?? throw AgentToolErrors.NoAnswerError();
        return all.FirstOrDefault(r => r.Id == id) ?? throw AgentToolErrors.NotFoundError("risk");
    }

    // ---- Findings --------------------------------------------------------------------------------------------------

    private static IEnumerable<IAgentTool> Findings(AgentEditServices s)
    {
        yield return new AgentEditTool("update_finding_status",
            "Move one finding between the open-work lifecycle states 'active' and 'verified', with an optional " +
            "justification. Suppressing, accepting, marking duplicate or closing a finding stays with the user.",
            AgentWritePermissions.FindingStatus,
            AgentSchema.Object(AgentSchema.Id("finding_id", "The finding (vulnerability) id."), VersionArgument,
                ("status", new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "string", ["enum"] = new System.Text.Json.Nodes.JsonArray("active", "verified"),
                    ["description"] = "The new lifecycle state."
                }, true),
                Text("justification", AgentEditLimits.NoteLength, "Why; recorded in the finding's lifecycle history.")),
            async (call, args, ct) =>
            {
                var id = args.Id("finding_id");
                var version = args.Version();
                var target = AgentEditNames.SettableLifecycle[args.Choice("status", AgentEditNames.SettableLifecycle.Keys.ToList())
                                                              ?? throw new AgentToolException(AgentToolErrors.Invalid,
                                                                  "Argument 'status' is required. Nothing was written.")];
                var justification = args.Text("justification", AgentEditLimits.NoteLength);

                var finding = await FreshFinding(s, id);
                await call.Classification.EnsureVisibleAsync(AgentItemLinks.ForFinding(finding), edit: true, ct);
                if (AgentVersions.Finding(finding) != version) throw AgentEditArgs.Stale("get_finding");

                var changes = finding.LifecycleStatus == target
                    ? new List<AgentFieldChange>()
                    : new List<AgentFieldChange>
                    {
                        new("lifecycle_status", AgentEditNames.Lifecycle(finding.LifecycleStatus), AgentEditNames.Lifecycle(target))
                    };
                if (changes.Count > 0 && justification is not null) changes.Add(new AgentFieldChange("justification", null, justification));
                return new AgentEditPlan(new AgentItemRef(AgentItemKinds.Finding, id), Label(finding.Title), changes, "updated",
                    async _ =>
                    {
                        if (AgentVersions.Finding(await FreshFinding(s, id)) != version)
                            throw AgentEditArgs.ChangedSincePrepared("get_finding");
                        var updated = await s.Findings.UpdateLifecycleStatusAsync(id, target, justification);
                        return updated is null ? null : AgentVersions.Finding(updated);
                    }, version);
            });

        yield return new AgentEditTool("update_finding_assignment",
            "Assign one finding to a fix team (a team id). Assigning an analyst, and every other field, stays with the user.",
            AgentWritePermissions.FindingAssignment,
            AgentSchema.Object(AgentSchema.Id("finding_id", "The finding (vulnerability) id."), VersionArgument,
                AgentSchema.Id("fix_team_id", "The team that should fix it.")),
            async (call, args, ct) =>
            {
                var id = args.Id("finding_id");
                var version = args.Version();
                var teamId = args.Id("fix_team_id");

                var finding = await FreshFinding(s, id);
                await call.Classification.EnsureVisibleAsync(AgentItemLinks.ForFinding(finding), edit: true, ct);
                if (AgentVersions.Finding(finding) != version) throw AgentEditArgs.Stale("get_finding");

                // The GUI picks the team from the team list; an id that is not on it is refused the same way.
                var teams = await s.Teams.GetAllAsync() ?? throw AgentToolErrors.NoAnswerError();
                var team = teams.FirstOrDefault(t => t.Value == teamId) ?? throw AgentToolErrors.NotFoundError("team");

                var changes = finding.FixTeamId == teamId
                    ? new List<AgentFieldChange>()
                    : new List<AgentFieldChange>
                    {
                        new("fix_team", TeamLabel(teams.FirstOrDefault(t => t.Value == finding.FixTeamId), finding.FixTeamId),
                            TeamLabel(team, teamId))
                    };
                return new AgentEditPlan(new AgentItemRef(AgentItemKinds.Finding, id), Label(finding.Title), changes, "updated",
                    async _ =>
                    {
                        var fresh = await FreshFinding(s, id);
                        if (AgentVersions.Finding(fresh) != version) throw AgentEditArgs.ChangedSincePrepared("get_finding");
                        fresh.FixTeamId = teamId;
                        fresh.FixTeam = null;
                        await s.Findings.UpdateAsync(fresh);
                        return await TryVersion(async () => AgentVersions.Finding(await FreshFinding(s, id)));
                    }, version);
            });

        yield return new AgentEditTool("add_finding_note",
            "Add a note to one finding's action log. Notes are appended; existing notes are never changed.",
            AgentWritePermissions.FindingNote,
            AgentSchema.Object(AgentSchema.Id("finding_id", "The finding (vulnerability) id."),
                (("note"), new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "string", ["maxLength"] = AgentEditLimits.ActionNoteLength,
                    ["description"] = "The note. " + StoredAsUserText
                }, true)),
            async (call, args, ct) =>
            {
                var id = args.Id("finding_id");
                // T354 fix: the note lands in nr_actions.Message (varchar(255)); a longer one was refused by the server
                // only after the user had approved it. Refused here instead, never cut.
                var note = args.Text("note", AgentEditLimits.ActionNoteLength, required: true)!;
                var finding = await FreshFinding(s, id);
                await call.Classification.EnsureVisibleAsync(AgentItemLinks.ForFinding(finding), edit: true, ct);
                var userId = call.User?.UserId ?? throw new AgentToolException("unavailable", "Nobody is signed in to NetRisk.");
                return new AgentEditPlan(new AgentItemRef(AgentItemKinds.Finding, id), Label(finding.Title),
                    new[] { new AgentFieldChange("note", null, note) }, "created",
                    async _ =>
                    {
                        await s.Findings.AddActionAsync(id, userId, new NrAction
                        {
                            DateTime = DateTime.Now, Message = note, ObjectType = nameof(Vulnerability), UserId = userId
                        });
                        return null;
                    }) { MovesVersion = false }; // T356 review: an append; the finding's record and version are as they were
            });
    }

    private static async Task<Vulnerability> FreshFinding(AgentEditServices s, int id) =>
        await s.Findings.GetOneAsync(id) ?? throw AgentToolErrors.NotFoundError("finding");

    private static string TeamLabel(Team? team, int? id) =>
        id is null ? "" : (Label(team?.Name) is { Length: > 0 } name ? name + " (#" + id + ")" : "#" + id);

    // ---- Incidents -------------------------------------------------------------------------------------------------

    private static IEnumerable<IAgentTool> Incidents(AgentEditServices s)
    {
        yield return new AgentEditTool("update_incident",
            "Change the descriptive text of one incident: description, report, impact, cause, solution, recommendations and " +
            "notes. Status, kind, assignment, people, dates and response plans stay with the user.",
            AgentWritePermissions.Incident,
            AgentSchema.Object(AgentSchema.Id("incident_id", "The incident id."), VersionArgument,
                Text("description", AgentEditLimits.TextLength, "New description; must not be empty."),
                Text("report", AgentEditLimits.TextLength, "New report text."),
                Text("impact", AgentEditLimits.TextLength, "New impact."),
                Text("cause", AgentEditLimits.TextLength, "New cause."),
                Text("solution", AgentEditLimits.TextLength, "New solution."),
                Text("recommendations", AgentEditLimits.TextLength, "New recommendations."),
                Text("notes", AgentEditLimits.TextLength, "New notes.")),
            async (call, args, ct) =>
            {
                var id = args.Id("incident_id");
                var version = args.Version();
                var updates = new (string Field, string? Value, Func<Incident, string?> Get, Action<Incident, string?> Set)[]
                {
                    // EditIncidentViewModel refuses to save without a description.
                    ("description", args.Text("description", AgentEditLimits.TextLength), i => i.Description, (i, v) => i.Description = v!),
                    ("report", args.Text("report", AgentEditLimits.TextLength, allowEmpty: true), i => i.Report, (i, v) => i.Report = Blank(v)),
                    ("impact", args.Text("impact", AgentEditLimits.TextLength, allowEmpty: true), i => i.Impact, (i, v) => i.Impact = Blank(v)),
                    ("cause", args.Text("cause", AgentEditLimits.TextLength, allowEmpty: true), i => i.Cause, (i, v) => i.Cause = Blank(v)),
                    ("solution", args.Text("solution", AgentEditLimits.TextLength, allowEmpty: true), i => i.Solution, (i, v) => i.Solution = Blank(v)),
                    ("recommendations", args.Text("recommendations", AgentEditLimits.TextLength, allowEmpty: true),
                        i => i.Recommendations, (i, v) => i.Recommendations = Blank(v)),
                    ("notes", args.Text("notes", AgentEditLimits.TextLength, allowEmpty: true), i => i.Notes, (i, v) => i.Notes = Blank(v))
                };
                if (updates.All(u => u.Value is null)) throw NothingToChange();

                var incident = await FreshIncident(s, id);
                await call.Classification.EnsureVisibleAsync(AgentItemLinks.ForIncident(incident), edit: true, ct);
                if (AgentVersions.Incident(incident) != version) throw AgentEditArgs.Stale("get_incident");

                var changes = Diff(incident, updates.Where(u => u.Value is not null).Select(u => (u.Field, u.Value, u.Get)));
                return new AgentEditPlan(new AgentItemRef(AgentItemKinds.Incident, id), Label(incident.Name), changes, "updated",
                    async _ =>
                    {
                        var fresh = await FreshIncident(s, id);
                        if (AgentVersions.Incident(fresh) != version) throw AgentEditArgs.ChangedSincePrepared("get_incident");
                        foreach (var u in updates.Where(u => u.Value is not null)) u.Set(fresh, u.Value);
                        var saved = await s.Incidents.UpdateAsync(fresh);
                        return saved is null ? null : await TryVersion(async () => AgentVersions.Incident(await FreshIncident(s, id)));
                    }, version);
            });

        yield return new AgentEditTool("create_incident",
            "Create one new incident with its description (required), report, impact, cause, solution, recommendations and " +
            "notes. NetRisk names it SI-<year>-<next number> as the screen does; it starts as New, category " +
            "'not_specified', kind Incident, reported now by the signed-in user, with no assignee and no reporting or " +
            "impacted entity: those stay with the user. On creation NetRisk itself may start the response plans whose " +
            "automation templates match the incident, and notifies incident-created subscribers. The user approves every " +
            "new incident on screen first, with that stated.",
            AgentWritePermissions.CreateIncident,
            AgentSchema.Object(
                Text("description", AgentEditLimits.TextLength, "The description; required."),
                Text("report", AgentEditLimits.TextLength, "Report text."),
                Text("impact", AgentEditLimits.TextLength, "Impact."),
                Text("cause", AgentEditLimits.TextLength, "Cause."),
                Text("solution", AgentEditLimits.TextLength, "Solution."),
                Text("recommendations", AgentEditLimits.TextLength, "Recommendations."),
                Text("notes", AgentEditLimits.TextLength, "Notes.")),
            async (call, args, ct) =>
            {
                var userId = call.User?.UserId ?? throw new AgentToolException("unavailable", "Nobody is signed in to NetRisk.");
                // EditIncidentViewModel refuses to save without a description.
                var description = args.Text("description", AgentEditLimits.TextLength, required: true)!;
                var texts = new (string Field, string? Value)[]
                {
                    ("report", args.Text("report", AgentEditLimits.TextLength, allowEmpty: true)),
                    ("impact", args.Text("impact", AgentEditLimits.TextLength, allowEmpty: true)),
                    ("cause", args.Text("cause", AgentEditLimits.TextLength, allowEmpty: true)),
                    ("solution", args.Text("solution", AgentEditLimits.TextLength, allowEmpty: true)),
                    ("recommendations", args.Text("recommendations", AgentEditLimits.TextLength, allowEmpty: true)),
                    ("notes", args.Text("notes", AgentEditLimits.TextLength, allowEmpty: true))
                };
                string? Value(string field) => Blank(texts.First(t => t.Field == field).Value);

                // The screen's naming: the next number of this year, read now and again at the write.
                var year = DateTime.Now.Year;
                var sequence = await s.Incidents.GetNextSequenceAsync(year);
                if (sequence < 1) throw AgentToolErrors.NoAnswerError();
                var name = IncidentName(year, sequence);

                var changes = new List<AgentFieldChange> { new("name", null, name), new("description", null, description) };
                changes.AddRange(texts.Where(t => !string.IsNullOrEmpty(t.Value)).Select(t => new AgentFieldChange(t.Field, null, t.Value)));
                changes.Add(new AgentFieldChange("status", null, "New"));
                changes.Add(new AgentFieldChange("category", null, NewIncidentCategory));
                changes.Add(new AgentFieldChange("kind", null, nameof(IncidentKind.Incident)));
                changes.Add(new AgentFieldChange("reported_by", null, "#" + userId + " (" + AgentSessionUserLabel(call) + ")"));
                changes.Add(new AgentFieldChange("duration", null, "1 h"));

                return AgentEditPlan.NewItem(AgentItemKinds.Incident, name, changes, async cancellation =>
                {
                    // Review M63 #2 for a creation: the name the user approved must still be the one NetRisk gives it.
                    if (await s.Incidents.GetNextSequenceAsync(year) != sequence)
                        throw new AgentToolException(AgentToolErrors.Stale,
                            "Another incident was created after your change was prepared, so this one would get another " +
                            "number. Nothing was written; send it again to prepare it with the new number.");
                    var now = DateTime.Now;
                    var created = await s.Incidents.CreateAsync(new Incident
                    {
                        Year = year, Sequence = sequence, Name = name, Description = description,
                        Report = Value("report"), Impact = Value("impact"), Cause = Value("cause"),
                        Solution = Value("solution"), Recommendations = Value("recommendations"), Notes = Value("notes"),
                        Status = 0, Category = NewIncidentCategory, Kind = IncidentKind.Incident,
                        CreationDate = now, LastUpdate = now, ReportDate = now, Duration = TimeSpan.FromHours(1),
                        CreatedById = userId, ReportedById = userId
                    }) ?? throw AgentToolErrors.NoAnswerError();
                    return await CreatedAsync(call, created.Id, () => FreshIncident(s, created.Id), AgentItemLinks.ForIncident,
                        AgentVersions.Incident, Array.Empty<string>(), cancellation);
                }, IncidentCreationSideEffectsKey);
            }, createsItems: true);

        yield return new AgentEditTool("add_incident_timeline_entry",
            "Append one dated entry to an incident's timeline: text only, stamped now as the signed-in user. Entries are " +
            "appended; nothing else of the incident changes and existing entries are never edited.",
            AgentWritePermissions.IncidentTimeline,
            AgentSchema.Object(AgentSchema.Id("incident_id", "The incident id."),
                ("text", new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "string", ["maxLength"] = AgentEditLimits.ActionNoteLength,
                    ["description"] = "The entry. " + StoredAsUserText
                }, true)),
            async (call, args, ct) =>
            {
                var id = args.Id("incident_id");
                var text = args.Text("text", AgentEditLimits.ActionNoteLength, required: true)!;
                var incident = await FreshIncident(s, id);
                await call.Classification.EnsureVisibleAsync(AgentItemLinks.ForIncident(incident), edit: true, ct);
                return new AgentEditPlan(new AgentItemRef(AgentItemKinds.Incident, id), Label(incident.Name),
                    new[] { new AgentFieldChange(TimelineEntryField, null, text) }, "created",
                    async _ =>
                    {
                        // Append-only (POST /Incidents/{id}/Timeline): the incident row and its version are not touched.
                        await s.Incidents.AddTimelineEntryAsync(id, text);
                        return null;
                    }) { MovesVersion = false };
            });
    }

    /// <summary>
    /// T354 review: what the server does itself when a new incident lands — IncidentsService.CreateAsync runs the IRP
    /// automation (TriggerForIncidentAsync) and the incident.created notification. Shown in the confirmation dialog.
    /// </summary>
    public const string IncidentCreationSideEffectsKey = "AgentCreateIncidentSideEffectsMSG";

    /// <summary>T354 review: RisksService.CreateRiskAsync sends the risk.created notification. Shown in the dialog.</summary>
    public const string RiskCreationSideEffectsKey = "AgentCreateRiskSideEffectsMSG";

    /// <summary>The diff line of an appended incident timeline entry (T354).</summary>
    public const string TimelineEntryField = "timeline_entry";

    /// <summary>A new incident's category on the screen until the user picks one (<c>Incident.Category</c>'s default).</summary>
    public const string NewIncidentCategory = "not_specified";

    /// <summary>EditIncidentViewModel.AdjustIncidentName: <c>SI-&lt;year&gt;-&lt;sequence:0000&gt;</c>.</summary>
    public static string IncidentName(int year, int sequence) =>
        "SI-" + year.ToString(CultureInfo.InvariantCulture) + "-" + sequence.ToString("0000.##", CultureInfo.InvariantCulture);

    private static async Task<Incident> FreshIncident(AgentEditServices s, int id)
    {
        // IIncidentsService has no single-incident read; the list is what EditIncidentViewModel edits.
        var all = await s.Incidents.GetAllAsync() ?? throw AgentToolErrors.NoAnswerError();
        return all.FirstOrDefault(i => i.Id == id) ?? throw AgentToolErrors.NotFoundError("incident");
    }

    // ---- Assessments -----------------------------------------------------------------------------------------------

    private static IEnumerable<IAgentTool> Assessments(AgentEditServices s)
    {
        yield return new AgentEditTool("answer_assessment_question",
            "Answer one unanswered question of an assessment run with one of the question's answer options. An answered " +
            "question is not changed: ask the user to clear it first.",
            AgentWritePermissions.Assessment,
            AgentSchema.Object(AgentSchema.Id("assessment_id", "The assessment id."), AgentSchema.Id("run_id", "The run id."),
                AgentSchema.Id("question_id", "The question id."), AgentSchema.Id("answer_id", "The chosen answer option id.")),
            async (call, args, ct) =>
            {
                var assessmentId = args.Id("assessment_id");
                var runId = args.Id("run_id");
                var questionId = args.Id("question_id");
                var answerId = args.Id("answer_id");

                var run = FreshRun(s, assessmentId, runId);
                await EnsureRunVisibleAsync(call, run, ct);
                var question = (s.Assessments.GetAssessmentQuestions(assessmentId) ?? throw AgentToolErrors.NoAnswerError())
                               .FirstOrDefault(q => q.Id == questionId) ?? throw AgentToolErrors.NotFoundError("question in this assessment");
                var option = (s.Assessments.GetAssessmentAnswers(assessmentId) ?? throw AgentToolErrors.NoAnswerError())
                             .FirstOrDefault(a => a.Id == answerId && a.QuestionId == questionId)
                             ?? throw AgentToolErrors.NotFoundError("answer option for this question");
                var given = s.Assessments.GetAssessmentRunAnsers(assessmentId, runId) ?? throw AgentToolErrors.NoAnswerError();
                if (given.Any(a => a.QuestionId == questionId))
                    throw new AgentToolException(AgentToolErrors.Invalid,
                        "This question is already answered in this run. Nothing was written; ask the user to change it.");

                return await Task.FromResult(new AgentEditPlan(new AgentItemRef(AgentItemKinds.AssessmentRun, run.Id),
                    Label(question.Question), new[] { new AgentFieldChange("answer", null, Label(option.Answer)) }, "created",
                    _ =>
                    {
                        // Review M63 #2: still unanswered now, not only when the plan was made.
                        var now = s.Assessments.GetAssessmentRunAnsers(assessmentId, runId) ?? throw AgentToolErrors.NoAnswerError();
                        if (now.Any(a => a.QuestionId == questionId))
                            throw new AgentToolException(AgentToolErrors.Stale,
                                "This question was answered after you prepared the change. Nothing was written; read the " +
                                "answers again with get_assessment_run_answers.");
                        s.Assessments.CreateRunAnswer(assessmentId, new AssessmentRunsAnswerDto
                        {
                            RunId = runId, QuestionId = questionId, AnswerId = answerId
                        });
                        return Task.FromResult<string?>(null);
                    }));
            });

        yield return new AgentEditTool("comment_assessment_run",
            "Replace the comments of one assessment run. The run's status, progress and answers are not changed. NetRisk " +
            "records the signed-in user as the run's analyst on every save, as the GUI does; the user sees that change too.",
            AgentWritePermissions.Assessment,
            AgentSchema.Object(AgentSchema.Id("assessment_id", "The assessment id."), AgentSchema.Id("run_id", "The run id."),
                VersionArgument,
                (("comments"), new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "string", ["maxLength"] = AgentEditLimits.NoteLength,
                    ["description"] = "The new comments. " + StoredAsUserText
                }, true)),
            async (call, args, ct) =>
            {
                var userId = call.User?.UserId ?? throw new AgentToolException("unavailable", "Nobody is signed in to NetRisk.");
                var assessmentId = args.Id("assessment_id");
                var runId = args.Id("run_id");
                var version = args.Version();
                var comments = args.Text("comments", AgentEditLimits.NoteLength, required: true)!;

                var run = FreshRun(s, assessmentId, runId);
                await EnsureRunVisibleAsync(call, run, ct);
                if (AgentVersions.AssessmentRun(run) != version) throw AgentEditArgs.Stale("list_assessment_runs");

                var changes = string.Equals(run.Comments, comments, StringComparison.Ordinal)
                    ? new List<AgentFieldChange>()
                    : new List<AgentFieldChange> { new("comments", run.Comments, comments) };
                // Review M63 #3: PUT /Assessments/{a}/runs/{r} sets the caller as the run's analyst (AssessmentsController),
                // a people field. When that changes it, the user approves it explicitly, as one more line of the diff.
                if (changes.Count > 0 && run.AnalystId != userId)
                    changes.Add(new AgentFieldChange(AnalystField, run.AnalystId is { } before ? "#" + before : null,
                        "#" + userId + " (" + AgentSessionUserLabel(call) + ")"));
                return await Task.FromResult(new AgentEditPlan(new AgentItemRef(AgentItemKinds.AssessmentRun, runId),
                    "#" + runId.ToString(CultureInfo.InvariantCulture), changes, "updated",
                    _ =>
                    {
                        var fresh = FreshRun(s, assessmentId, runId);
                        if (AgentVersions.AssessmentRun(fresh) != version)
                            throw AgentEditArgs.ChangedSincePrepared("list_assessment_runs");
                        s.Assessments.UpdateAssessmentRun(new AssessmentRunDto
                        {
                            Id = fresh.Id, AssessmentId = fresh.AssessmentId, EntityId = fresh.EntityId, HostId = fresh.HostId,
                            RunDate = fresh.RunDate, AnalystId = fresh.AnalystId, Status = fresh.Status,
                            ProgressPercentage = fresh.ProgressPercentage, CurrentPageIndex = fresh.CurrentPageIndex,
                            Comments = comments
                        });
                        // UpdateAssessmentRun logs and swallows a refusal: read back, and say so when it did not land.
                        var after = FreshRun(s, assessmentId, runId);
                        if (!string.Equals(after.Comments, comments, StringComparison.Ordinal))
                            throw AgentToolErrors.NoAnswerError();
                        return Task.FromResult<string?>(AgentVersions.AssessmentRun(after));
                    }, version));
            });
    }

    /// <summary>The diff line naming the run's analyst change the server makes on every run save (review M63 #3).</summary>
    public const string AnalystField = "analyst";

    private static string AgentSessionUserLabel(AgentToolCall call) =>
        AgentResultLimits.Clip(call.User?.UserName ?? call.User?.UserAccount, 80) ?? "";

    /// <summary>T362: the run's own entity or host, and its assessment's entity (<see cref="AgentItemLinks.ForRun"/>).</summary>
    private static Task EnsureRunVisibleAsync(AgentToolCall call, AssessmentRun run, System.Threading.CancellationToken ct) =>
        call.Classification.EnsureVisibleAsync(AgentItemLinks.ForRun(run), edit: true, ct);

    private static AssessmentRun FreshRun(AgentEditServices s, int assessmentId, int runId) =>
        (s.Assessments.GetAssessmentRuns(assessmentId) ?? throw AgentToolErrors.NoAnswerError())
        .FirstOrDefault(r => r.Id == runId) ?? throw AgentToolErrors.NotFoundError("run of this assessment");

    // ---- Entities and hosts ----------------------------------------------------------------------------------------

    private static IEnumerable<IAgentTool> Inventory(AgentEditServices s)
    {
        yield return new AgentEditTool("update_entity_fields",
            "Change the descriptive text of one entity: name, description, objective or technology, where its type has " +
            "them. Risk context (criticality, internet exposure, classification), parent, status, links and people's " +
            "records stay with the user.",
            AgentWritePermissions.Entity,
            AgentSchema.Object(AgentSchema.Id("entity_id", "The entity id."), VersionArgument,
                Text("name", AgentEditLimits.TitleLength, "New name; must not be empty."),
                Text("description", AgentEditLimits.TextLength, "New description."),
                Text("objective", AgentEditLimits.TextLength, "New objective."),
                Text("technology", AgentEditLimits.TitleLength, "New technology.")),
            async (call, args, ct) =>
            {
                var id = args.Id("entity_id");
                var version = args.Version();
                var values = EditableEntityProperties
                    .Select(p => (Key: p.Key, Value: args.Text(p.Key, p.Value, allowEmpty: p.Key != "name")))
                    .Where(p => p.Value is not null).ToList();
                if (values.Count == 0) throw NothingToChange();

                // Always from the API with this session; IEntitiesService.GetEntity reads a process-wide cache.
                var entity = (await s.Entities.GetAllFromServerAsync() ?? throw AgentToolErrors.NoAnswerError())
                             .FirstOrDefault(e => e.Id == id) ?? throw AgentToolErrors.NotFoundError("entity");
                await call.Classification.EnsureVisibleAsync(AgentItemLinks.ForEntity(entity.Id), edit: true, ct);
                if (AgentVersions.Entity(entity) != version) throw AgentEditArgs.Stale("get_entity");
                if (ProtectedDefinitions.Contains(entity.DefinitionName))
                    throw new AgentToolException(AgentToolErrors.Refused,
                        "A person's record is personal data and is edited only by the user. Nothing was written.");

                var properties = (entity.EntitiesProperties ?? new List<EntitiesProperty>()).ToList();
                var changes = new List<AgentFieldChange>();
                foreach (var (key, value) in values)
                {
                    var rows = properties.Where(p => p.Type == key).ToList();
                    if (rows.Count > 1)
                        throw new AgentToolException(AgentToolErrors.Invalid,
                            $"Argument '{key}' holds several values on this entity; ask the user. Nothing was written.");
                    if (rows.Count == 1 && AgentDataPolicy.Screen(key, rows[0].Value) is not null)
                        throw new AgentToolException(AgentToolErrors.Refused,
                            $"The current '{key}' is withheld from agents, so it is not changed by one. Nothing was written.");
                    var before = rows.FirstOrDefault()?.Value;
                    if (!string.Equals(before ?? "", value, StringComparison.Ordinal)) changes.Add(new AgentFieldChange(key, before, value));
                }

                var name = properties.FirstOrDefault(p => p.Type == "name")?.Value;
                return new AgentEditPlan(new AgentItemRef(AgentItemKinds.Entity, id), Label(name), changes, "updated",
                    async _ =>
                    {
                        // Review M63 #2: the PUT carries the complete property set, so it is built from a copy read now,
                        // and only if the entity has not changed since the plan was made (Updated moves on every save).
                        var fresh = (await s.Entities.GetAllFromServerAsync() ?? throw AgentToolErrors.NoAnswerError())
                                    .FirstOrDefault(e => e.Id == id) ?? throw AgentToolErrors.NotFoundError("entity");
                        if (AgentVersions.Entity(fresh) != version) throw AgentEditArgs.ChangedSincePrepared("get_entity");
                        var current = (fresh.EntitiesProperties ?? new List<EntitiesProperty>()).ToList();
                        var dto = new EntityDto
                        {
                            Id = fresh.Id, DefinitionName = fresh.DefinitionName, Status = fresh.Status,
                            Parent = fresh.Parent,
                            EntitiesProperties = current.Select(p => new EntitiesPropertyDto
                            {
                                Id = p.Id, Name = p.Name, Type = p.Type,
                                Value = changes.FirstOrDefault(c => c.Field == p.Type) is { } c ? c.After! : p.Value
                            }).ToList()
                        };
                        foreach (var added in changes.Where(c => current.All(p => p.Type != c.Field)))
                            dto.EntitiesProperties.Add(new EntitiesPropertyDto
                            {
                                Type = added.Field, Name = $"{added.Field}-{fresh.Id}", Value = added.After!
                            });
                        if (await s.Entities.SaveEntityAsync(dto) is null) throw AgentToolErrors.NoAnswerError();
                        return await TryVersion(async () => AgentVersions.Entity(
                            (await s.Entities.GetAllFromServerAsync()).First(e => e.Id == id)));
                    }, version);
            });

        yield return new AgentEditTool("update_host_fields",
            "Change the comment or the operating-system text of one host. Name, addresses, status, team, entity and " +
            "scanner properties stay with the user and the imports.",
            AgentWritePermissions.Host,
            AgentSchema.Object(AgentSchema.Id("host_id", "The host id."), VersionArgument,
                Text("comment", AgentEditLimits.NoteLength, "New comment."),
                Text("os", 255, "New operating-system text.")),
            async (call, args, ct) =>
            {
                var id = args.Id("host_id");
                var version = args.Version();
                var comment = args.Text("comment", AgentEditLimits.NoteLength, allowEmpty: true);
                var os = args.Text("os", 255, allowEmpty: true);
                if (comment is null && os is null) throw NothingToChange();

                var host = await FreshHost(s, id);
                await call.Classification.EnsureVisibleAsync(AgentItemLinks.ForHost(host), edit: true, ct);
                if (AgentVersions.Host(host) != version) throw AgentEditArgs.Stale("get_host");

                var changes = new List<AgentFieldChange>();
                if (comment is not null && !string.Equals(host.Comment ?? "", comment, StringComparison.Ordinal))
                    changes.Add(new AgentFieldChange("comment", host.Comment, comment));
                if (os is not null && !string.Equals(host.Os ?? "", os, StringComparison.Ordinal))
                    changes.Add(new AgentFieldChange("os", host.Os, os));
                return new AgentEditPlan(new AgentItemRef(AgentItemKinds.Host, id), Label(host.HostName ?? host.Ip), changes,
                    "updated",
                    async _ =>
                    {
                        var fresh = await FreshHost(s, id);
                        if (AgentVersions.Host(fresh) != version) throw AgentEditArgs.ChangedSincePrepared("get_host");
                        if (comment is not null) fresh.Comment = Blank(comment);
                        if (os is not null) fresh.Os = Blank(os);
                        await s.Hosts.UpdateHostAsync(fresh);
                        return await TryVersion(async () => AgentVersions.Host(await FreshHost(s, id)));
                    }, version);
            });
    }

    private static async Task<Host> FreshHost(AgentEditServices s, int id)
    {
        // The filtered list endpoint, by id: always the API with this session (IHostsService.GetOne reads a cache).
        var (items, _) = await s.Hosts.GetFilteredAsync(1, 1, Tools.Navigation.RecordNavigation.HostFilter(id), null);
        return items?.FirstOrDefault(h => h.Id == id) ?? throw AgentToolErrors.NotFoundError("host");
    }

    // ---- shared ----------------------------------------------------------------------------------------------------

    private static List<AgentFieldChange> Diff<T>(T item, IEnumerable<(string Field, string? Value, Func<T, string?> Get)> updates) =>
        updates.Where(u => !string.Equals(u.Get(item) ?? "", u.Value ?? "", StringComparison.Ordinal))
            .Select(u => new AgentFieldChange(u.Field, u.Get(item), u.Value)).ToList();

    private static string? Blank(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string Label(string? value) => AgentResultLimits.Clip(value, AgentResultLimits.TitleLength) ?? "";

    private static AgentToolException NothingToChange() =>
        new(AgentToolErrors.Invalid, "No field to change was given. Nothing was written.");

    /// <summary>The item's version after the write; null (not a failure) when the read-back itself fails.</summary>
    private static async Task<string?> TryVersion(Func<Task<string>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>The whole reviewed tool surface the GUI composes: the M62 read tools and the M63 edit tools.</summary>
public static class AgentToolCatalog
{
    public static IReadOnlyList<IAgentTool> Create(IServiceProvider services) =>
        AgentReadToolCatalog.Create(services).Concat(AgentEditToolCatalog.Create(services)).ToList();
}
