using System.Linq;
using DAL.Entities;

namespace GUIClient.Agents.ReadTools;

/// <summary>
/// The version (<see cref="AgentItemVersion"/>) of each item an edit tool can change, computed the same way by the read
/// tool that hands it out and by the edit tool that checks it. Only fields the agent can read go in — never a withheld
/// entity property, a host's free-form properties or anything credential-like — so the digest reveals nothing.
/// </summary>
public static class AgentVersions
{
    public static string Risk(Risk r) => AgentItemVersion.Of(new AgentItemRef(AgentItemKinds.Risk, r.Id),
        r.Subject, r.Status, r.Notes, r.Assessment, r.ScenarioCause, r.ScenarioVulnerability, r.ScenarioCentralEvent,
        r.ScenarioConsequences, r.LastUpdate, r.Owner, r.Manager, r.Category, r.Source, r.EntityId);

    public static string Finding(Vulnerability v) => AgentItemVersion.Of(new AgentItemRef(AgentItemKinds.Finding, v.Id),
        v.Status, (int)v.LifecycleStatus, v.FixTeamId, v.AnalystId, v.Comments, v.Title, v.Severity, v.LastDetection);

    public static string Incident(Incident i) => AgentItemVersion.Of(new AgentItemRef(AgentItemKinds.Incident, i.Id),
        i.LastUpdate, i.Status, i.Name, i.Description, i.Impact, i.Cause, i.Solution, i.Recommendations, i.Notes, i.Report,
        i.AssignedToId, (int)i.Kind);

    public static string AssessmentRun(AssessmentRun r) =>
        AgentItemVersion.Of(new AgentItemRef(AgentItemKinds.AssessmentRun, r.Id), r.Status, r.Comments, r.AnalystId,
            r.ProgressPercentage, r.RunDate);

    /// <summary>The entity's own stamp, status and parent, and only the properties the data policy lets an agent see.</summary>
    public static string Entity(Entity e)
    {
        var visible = (e.EntitiesProperties ?? System.Array.Empty<EntitiesProperty>())
            .Where(p => AgentDataPolicy.Screen(p.Type, p.Value) is null)
            .OrderBy(p => p.Type, System.StringComparer.Ordinal).ThenBy(p => p.Value, System.StringComparer.Ordinal)
            .SelectMany(p => new object?[] { p.Type, p.Value });
        return AgentItemVersion.Of(new AgentItemRef(AgentItemKinds.Entity, e.Id),
            new object?[] { e.Updated, e.Status, e.Parent, e.DefinitionName }.Concat(visible).ToArray());
    }

    public static string Host(Host h) => AgentItemVersion.Of(new AgentItemRef(AgentItemKinds.Host, h.Id),
        h.HostName, h.Ip, h.Fqdn, h.Os, h.MacAddress, h.Status, h.TeamId, h.EntityId, h.Comment, h.LastVerificationDate);
}
