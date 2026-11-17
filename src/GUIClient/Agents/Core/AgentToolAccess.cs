using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using GUIClient.Models;
using GUIClient.Tools;
using GUIClient.Tools.Navigation;
using Model.Authentication;

namespace GUIClient.Agents;

/// <summary>
/// What the signed-in user must hold for an agent tool to be listed and to run (T350, S55). Built only from the GUI's
/// own access rules — <see cref="ModuleAccess"/> and <see cref="AdminRoleClaim"/> — over the permission list the client
/// already holds for the session (<c>IAuthenticationService.AuthenticatedUserInfo</c>); there is no parallel source.
/// A restriction, never an authority: the API decides every call a tool makes. It fails closed.
/// </summary>
public sealed class AgentToolPermission
{
    private readonly Func<AuthenticatedUserInfo, bool> _isHeldBy;

    private AgentToolPermission(string description, IReadOnlyList<string> keys, Func<AuthenticatedUserInfo, bool> isHeldBy)
    {
        Description = description;
        Keys = keys;
        _isHeldBy = isHeldBy;
    }

    /// <summary>Human-readable rule, for the inventory test and the spec.</summary>
    public string Description { get; }

    /// <summary>
    /// Every permission key the rule reads. A user holding all of them passes; a non-administrator holding none of them
    /// fails (except under <see cref="SignedIn"/>, which reads none).
    /// </summary>
    public IReadOnlyList<string> Keys { get; }

    public bool IsHeldBy(AuthenticatedUserInfo? user) => user is not null && _isHeldBy(user);

    /// <summary>Any signed-in user: only for tools that describe the session itself (<c>whoami</c>).</summary>
    public static AgentToolPermission SignedIn { get; } = new("signed-in user", Array.Empty<string>(), _ => true);

    /// <summary>The navigation bar's rule for <paramref name="view"/> (<see cref="ModuleAccess.CanOpen"/>).</summary>
    public static AgentToolPermission Module(AvaliableViews view)
    {
        var key = ModuleAccess.RequiredPermission(view)
                  ?? throw new ArgumentException($"Module {view} has no permission to gate an agent tool on.", nameof(view));
        return new AgentToolPermission($"module {view} ({key})", new[] { key }, user => ModuleAccess.CanOpen(view, user));
    }

    /// <summary>
    /// The read mirror of <c>[PermissionAuthorize(key)]</c>: the permission or the <c>Admin</c> role claim, which
    /// <c>PermissionAuthorizationHandler</c> admits for any permission (the rule of <c>RiskIndexAccess.CanView</c>).
    /// </summary>
    public static AgentToolPermission Granted(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A permission key is required.", nameof(key));
        return new AgentToolPermission($"permission {key}", new[] { key },
            user => AdminRoleClaim.IsHeldBy(user) || Holds(user, key));
    }

    /// <summary>
    /// The mirror of <c>RequireClaim("Permission", key)</c> (e.g. <c>RequireAssessmentAccess</c>): the permission itself,
    /// with no administrator shortcut, because the server grants none.
    /// </summary>
    public static AgentToolPermission Claim(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A permission key is required.", nameof(key));
        return new AgentToolPermission($"claim {key}", new[] { key }, user => Holds(user, key));
    }

    /// <summary>
    /// The mirror of a policy admitting one role by name or one permission (e.g. <c>RequireRiskmanagement</c>: the
    /// <c>Administrator</c> role or <c>riskmanagement</c>). The admin flag's <c>Admin</c> claim is not that role.
    /// </summary>
    public static AgentToolPermission RoleOrPermission(string role, string key)
    {
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A role and a permission key are required.");
        return new AgentToolPermission($"role {role} or permission {key}", new[] { key },
            user => string.Equals(user.UserRole, role, StringComparison.Ordinal) || Holds(user, key));
    }

    /// <summary>
    /// The mirror of a claims requirement listing several permissions (e.g. <c>RequireMgmtReviewAccess</c>): any one of
    /// them, with no administrator shortcut, because the server grants none.
    /// </summary>
    public static AgentToolPermission AnyOf(params string[] keys)
    {
        if (keys.Length == 0 || keys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one permission key is required.", nameof(keys));
        var copy = keys.ToArray();
        return new AgentToolPermission("any of " + string.Join(", ", copy), copy,
            user => copy.Any(key => Holds(user, key)));
    }

    /// <summary>
    /// The mirror of <c>ThirdLineReadOnlyRequirement</c>, which every policy the API builds carries: the third line
    /// (internal audit) is refused every write, administrators included. Reads no key a user must hold, so it adds none.
    /// </summary>
    public static AgentToolPermission NotThirdLine { get; } = new("not the third line", Array.Empty<string>(),
        user => !Holds(user, Model.DecisionCycle.ThirdLineAssurance.PermissionKey));

    /// <summary>Both rules.</summary>
    public AgentToolPermission And(AgentToolPermission other) =>
        new(Description + " and " + other.Description, Keys.Concat(other.Keys).Distinct(StringComparer.Ordinal).ToArray(),
            user => IsHeldBy(user) && other.IsHeldBy(user));

    public override string ToString() => Description;

    private static bool Holds(AuthenticatedUserInfo user, string permission) =>
        user.UserPermissions?.Any(p => string.Equals(p, permission, StringComparison.Ordinal)) ?? false;
}

/// <summary>
/// The signed-in user for the agent gate: <c>AuthenticatedUserInfo</c> only while the session is authenticated. Logout
/// and an expired token clear <c>IsAuthenticated</c> but leave the user info set (M62 security review).
/// </summary>
public static class AgentSessionUser
{
    public static AuthenticatedUserInfo? Of(ClientServices.Interfaces.IAuthenticationService authentication) =>
        authentication.IsAuthenticated ? authentication.AuthenticatedUserInfo : null;
}

/// <summary>One entry of the reviewed tool surface: the name, the permission rule it is gated on and its read/edit kind.</summary>
public sealed record ReviewedAgentTool(string Name, string Permission, bool RequiresEdit);

/// <summary>
/// The reviewed tool surface (T353, S55 "Never exposed"). The registry refuses any tool that is not here, or whose rule
/// or read/edit kind differs, so a tool composed anywhere — catalog, bootstrapper, plugin — fails closed until it is
/// reviewed. Each rule mirrors the API policy of the endpoints the tool calls (cited per group).
/// </summary>
public static class AgentToolInventory
{
    private const string RiskRule = "role Administrator or permission riskmanagement"; // RequireRiskmanagement
    private const string AssessmentRule = "claim assessments";                           // RequireAssessmentAccess
    private const string NotThirdLine = " and not the third line";                         // ThirdLineReadOnlyRequirement

    public static IReadOnlyDictionary<string, ReviewedAgentTool> Reviewed { get; } = new[]
    {
        new ReviewedAgentTool("whoami", "signed-in user", false),
        new ReviewedAgentTool("search_risks", RiskRule, false),
        new ReviewedAgentTool("get_risk", RiskRule, false),
        new ReviewedAgentTool("get_risk_mitigation", RiskRule, false),
        // T354: GET /Risks/Categories and /Sources need a valid user; kept at the risk module's rule (stricter).
        new ReviewedAgentTool("list_risk_categories_and_sources", RiskRule, false),
        // RequireMgmtReviewAccess: one of the review permissions, no role.
        new ReviewedAgentTool("list_risk_reviews",
            "any of review_insignificant, review_low, review_medium, review_high, review_veryhigh", false),
        // [PermissionAuthorize("vulnerabilities")] on VulnerabilitiesController.
        new ReviewedAgentTool("search_findings", "permission vulnerabilities", false),
        new ReviewedAgentTool("get_finding", "permission vulnerabilities", false),
        new ReviewedAgentTool("get_finding_lifecycle", "permission vulnerabilities", false),
        // [PermissionAuthorize("incident_management")] on IncidentsController (GET /Incidents also needs FaceID when on).
        new ReviewedAgentTool("search_incidents", "permission incident_management", false),
        new ReviewedAgentTool("get_incident", "permission incident_management", false),
        // [PermissionAuthorize("incident-response-plans")] on IncidentResponsePlansController.
        new ReviewedAgentTool("search_response_plans", "permission incident-response-plans", false),
        new ReviewedAgentTool("get_response_plan", "permission incident-response-plans", false),
        new ReviewedAgentTool("list_assessments", AssessmentRule, false),
        new ReviewedAgentTool("list_assessment_runs", AssessmentRule, false),
        new ReviewedAgentTool("get_assessment_run_answers", AssessmentRule, false),
        new ReviewedAgentTool("list_assessment_evidence", AssessmentRule, false),
        // EntitiesController reads need only a valid user; the tools keep the GUI's stricter module rule.
        new ReviewedAgentTool("search_entities", "module Entities (asset)", false),
        new ReviewedAgentTool("get_entity", "module Entities (asset)", false),
        new ReviewedAgentTool("get_entity_relationships", "module Entities (asset)", false),
        // [PermissionAuthorize("hosts")] on HostsController.
        new ReviewedAgentTool("search_hosts", "permission hosts", false),
        new ReviewedAgentTool("get_host", "permission hosts", false),
        // [PermissionAuthorize(risk_index_view)] on RiskIndexController.
        new ReviewedAgentTool("get_risk_overview", "permission risk_index_view", false),
        new ReviewedAgentTool("list_risk_overview_children", "permission risk_index_view", false),

        // ---- M63 edit tools (T354). Each rule covers the read the tool makes first and the write, and the third line's
        // read-only rule that every API policy carries. Excluded fields and states: spec "Implementation notes (M63)".
        // GET /Risks (RequireRiskmanagement), GET /Risks/Exists and PUT /Risks/{id} (RequireSubmitRisk).
        new ReviewedAgentTool("update_risk", RiskRule + " and claim submit_risks" + NotThirdLine, true),
        // PUT /Vulnerabilities/{id}/status: class vulnerabilities + vulnerabilities_update.
        new ReviewedAgentTool("update_finding_status",
            "permission vulnerabilities and permission vulnerabilities_update" + NotThirdLine, true),
        // PUT /Vulnerabilities/{id} + POST {id}/RisksAssociate: vulnerabilities + vulnerabilities_create; GET /Teams any user.
        new ReviewedAgentTool("update_finding_assignment",
            "permission vulnerabilities and permission vulnerabilities_create" + NotThirdLine, true),
        // POST /Vulnerabilities/{id}/Actions: the class rule only.
        new ReviewedAgentTool("add_finding_note", "permission vulnerabilities" + NotThirdLine, true),
        // PUT /Incidents/{id}: class incident_management (GET /Incidents also needs FaceID when on).
        new ReviewedAgentTool("update_incident", "permission incident_management" + NotThirdLine, true),
        // POST answers, PUT runs/{id}: RequireAssessmentAccess.
        new ReviewedAgentTool("answer_assessment_question", AssessmentRule + NotThirdLine, true),
        new ReviewedAgentTool("comment_assessment_run", AssessmentRule + NotThirdLine, true),
        // PUT /Entities/{id}: [PermissionAuthorize(entities_manage)].
        new ReviewedAgentTool("update_entity_fields", "permission entities_manage" + NotThirdLine, true),
        // PUT /Hosts/{id}: class hosts + hosts_create.
        new ReviewedAgentTool("update_host_fields", "permission hosts and permission hosts_create" + NotThirdLine, true),

        // ---- T354 remainder. Creations always wait for the user (IAgentEditTool.CreatesItems); no person field is set
        // by the agent. POST /Risks + /Risks/{id}/Scoring + GET /Risks/Exists (RequireSubmitRisk), GET /Risks/ScoreValue
        // and GET /Risks (RequireRiskmanagement), the category and source lists (valid user).
        new ReviewedAgentTool("create_risk", RiskRule + " and claim submit_risks" + NotThirdLine, true),
        // GET /Incidents/NextSequence and POST /Incidents: class incident_management, as PUT.
        new ReviewedAgentTool("create_incident", "permission incident_management" + NotThirdLine, true),
        // POST /Incidents/{id}/Timeline (new, T354): class incident_management, as PUT; GET /Incidents first.
        new ReviewedAgentTool("add_incident_timeline_entry", "permission incident_management" + NotThirdLine, true)
    }.ToDictionary(t => t.Name, StringComparer.Ordinal);
}

/// <summary>
/// The business tools the dispatcher serves (T350), each with its name, read/edit kind, required permission and result
/// shape. Built once: a tool that is not reviewed (<see cref="AgentToolInventory"/>), differs from its review, or declares
/// a result shape that is not a clean allowlist (<see cref="AgentResultShape"/>) is refused. <see cref="VisibleTo"/> is
/// what <c>tools/list</c> shows the signed-in user.
/// </summary>
public sealed class AgentToolRegistry
{
    /// <summary>Lower-case snake case, 3–48 characters: what an MCP client shows and what the inventory test reads.</summary>
    public static readonly Regex NamePattern = new("^[a-z][a-z0-9_]{2,47}$", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.Ordinal);

    public AgentToolRegistry(IEnumerable<IAgentTool>? tools, IEnumerable<string> reservedNames,
        IReadOnlyDictionary<string, ReviewedAgentTool>? reviewed = null)
    {
        var reserved = new HashSet<string>(reservedNames, StringComparer.Ordinal);
        reviewed ??= AgentToolInventory.Reviewed;
        foreach (var tool in tools ?? Array.Empty<IAgentTool>())
        {
            if (tool.Name is null || !NamePattern.IsMatch(tool.Name))
                throw new ArgumentException($"Tool name '{tool.Name}' is not lower-case snake case.", nameof(tools));
            if (reserved.Contains(tool.Name) || !_tools.TryAdd(tool.Name, tool))
                throw new ArgumentException($"Tool name '{tool.Name}' is reserved or registered twice.", nameof(tools));
            if (tool.Permission is null || tool.ResultType is null)
                throw new ArgumentException($"Tool '{tool.Name}' declares no permission or result shape.", nameof(tools));
            if (!reviewed.TryGetValue(tool.Name, out var review))
                throw new ArgumentException($"Tool '{tool.Name}' is not on the reviewed tool list.", nameof(tools));
            if (review.Permission != tool.Permission.Description || review.RequiresEdit != tool.RequiresEdit)
                throw new ArgumentException($"Tool '{tool.Name}' differs from its review ('{tool.Permission.Description}', " +
                                            $"edit={tool.RequiresEdit}).", nameof(tools));
            // T357: an edit is applied only by the dispatcher, after the confirmation gate, so it must arrive as a plan.
            if (tool.RequiresEdit != tool is IAgentEditTool)
                throw new ArgumentException($"Tool '{tool.Name}' must be an IAgentEditTool exactly when it edits.", nameof(tools));
            if (AgentResultShape.Problems(tool.ResultType) is { Count: > 0 } problems)
                throw new ArgumentException($"Tool '{tool.Name}' declares a leaky result shape: {problems[0]}.", nameof(tools));
        }
        Tools = _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Every registered tool, by name.</summary>
    public IReadOnlyList<IAgentTool> Tools { get; }

    public bool TryGet(string name, [NotNullWhen(true)] out IAgentTool? tool) => _tools.TryGetValue(name, out tool);

    /// <summary>The tools <paramref name="user"/> holds the permission for; none without a user.</summary>
    public IEnumerable<IAgentTool> VisibleTo(AuthenticatedUserInfo? user) => Tools.Where(t => t.Permission.IsHeldBy(user));
}
