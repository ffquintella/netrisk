using System;
using System.Collections.Generic;
using System.Linq;

namespace GUIClient.Agents;

/// <summary>
/// T356: what one screen highlights for one item — the controls showing a field an agent changed that the user has not
/// acknowledged or edited, the agent, and the tooltip text those controls carry (so colour is never the only signal).
/// </summary>
public sealed class AgentFieldHighlights
{
    public static AgentFieldHighlights None { get; } =
        new(new HashSet<string>(StringComparer.Ordinal), Array.Empty<string>(), Array.Empty<string>(), null, null);

    public AgentFieldHighlights(IReadOnlySet<string> controls, IReadOnlyList<string> fields,
        IReadOnlyList<string> notShown, string? agentName, string? tip)
    {
        Controls = controls;
        Fields = fields;
        NotShown = notShown;
        AgentName = agentName;
        Tip = tip;
    }

    /// <summary>The control identities this screen lights.</summary>
    public IReadOnlySet<string> Controls { get; }

    /// <summary>Every changed field not yet acknowledged, as the agent's tools name them (the notice lists them).</summary>
    public IReadOnlyList<string> Fields { get; }

    /// <summary>Changed fields this screen has no control for: named in the notice only.</summary>
    public IReadOnlyList<string> NotShown { get; }

    public string? AgentName { get; }

    public string? Tip { get; }

    public bool Any => Fields.Count > 0;

    public bool IsLit(string control) => Controls.Contains(control);

    /// <summary>The tooltip for <paramref name="control"/>: the text when it is lit, otherwise null (no tooltip).</summary>
    public string? TipFor(string control) => IsLit(control) ? Tip : null;
}

/// <summary>
/// T356: which control of a screen shows which field the agent tools write (<see cref="AgentFieldChange.Field"/>). A field
/// is either mapped to a control identity — the view binds that identity — or declared <see cref="NotShown"/> on this
/// screen. <c>AgentScreenGuardTest</c> runs every edit tool and fails on a field a screen of its kind does neither for.
/// </summary>
public sealed class AgentHighlightScreen
{
    public AgentHighlightScreen(string name, string kind, IReadOnlyDictionary<string, string> controls,
        IReadOnlyCollection<string> notShown)
    {
        Name = name;
        Kind = kind;
        Controls = controls;
        NotShown = new HashSet<string>(notShown, StringComparer.Ordinal);
    }

    public string Name { get; }

    public string Kind { get; }

    /// <summary>Field → control identity (the <c>ConverterParameter</c> the view passes).</summary>
    public IReadOnlyDictionary<string, string> Controls { get; }

    public IReadOnlySet<string> NotShown { get; }

    /// <summary>Whether this screen accounts for <paramref name="field"/> (a control, or named in the notice only).</summary>
    public bool Covers(string field) => Controls.ContainsKey(field) || NotShown.Contains(field);

    /// <summary>The highlights for an item's unacknowledged change (<see cref="AgentPresence.Unacknowledged"/>).</summary>
    public AgentFieldHighlights StateFor(AgentItemChange? pending, string? tip)
    {
        if (pending is null || pending.Item.Kind != Kind || pending.Fields.Count == 0) return AgentFieldHighlights.None;
        var controls = pending.Fields.Where(Controls.ContainsKey).Select(f => Controls[f])
            .ToHashSet(StringComparer.Ordinal);
        // A field the map does not know is still named (fail visible): it is reported as not shown, never dropped.
        var notShown = pending.Fields.Where(f => !Controls.ContainsKey(f)).ToList();
        return new AgentFieldHighlights(controls, pending.Fields.ToList(), notShown, pending.AgentName, tip);
    }
}

/// <summary>T356: the screens that highlight an agent's changes, one map each.</summary>
public static class AgentHighlightScreens
{
    private static Dictionary<string, string> Map(params (string Field, string Control)[] pairs) =>
        pairs.ToDictionary(p => p.Field, p => p.Control, StringComparer.Ordinal);

    /// <summary>The risk register's detail panel (RiskView).</summary>
    public static AgentHighlightScreen Risks { get; } = new("risks", AgentItemKinds.Risk, Map(
        ("subject", "Subject"), ("notes", "Notes"), ("scenario_cause", "ScenarioCause"),
        ("scenario_vulnerability", "ScenarioVulnerability"), ("scenario_central_event", "ScenarioCentralEvent"),
        ("scenario_consequences", "ScenarioConsequences"), ("category", "Category"), ("source", "Source"),
        ("status", "Status"), ("owner", "Owner"), ("score", "Score")), Array.Empty<string>());

    /// <summary>The incident detail panel (IncidentsView). It shows no category, no reporting user and no timeline.</summary>
    public static AgentHighlightScreen Incidents { get; } = new("incidents", AgentItemKinds.Incident, Map(
        ("name", "Name"), ("description", "Description"), ("report", "Report"), ("impact", "Impact"), ("cause", "Cause"),
        ("solution", "Solution"), ("recommendations", "Recommendations"), ("notes", "Notes"), ("status", "Status"),
        ("kind", "Kind"), ("duration", "Duration")), new[] { "category", "reported_by", "timeline_entry" });

    /// <summary>The finding detail panel (VulnerabilitiesView): the lifecycle shows in its timeline, notes in its actions.</summary>
    public static AgentHighlightScreen Findings { get; } = new("findings", AgentItemKinds.Finding, Map(
        ("lifecycle_status", "Timeline"), ("justification", "Timeline"), ("fix_team", "FixTeam"), ("note", "Actions")),
        Array.Empty<string>());

    /// <summary>The host detail panel (HostsView).</summary>
    public static AgentHighlightScreen Hosts { get; } = new("hosts", AgentItemKinds.Host, Map(
        ("comment", "Comment"), ("os", "Os")), Array.Empty<string>());

    /// <summary>The entity form (EntityForm): a field's control is the property field of the same key.</summary>
    public static AgentHighlightScreen EntityForm { get; } = new("entity_form", AgentItemKinds.Entity, Map(
        ("name", "name"), ("description", "description"), ("objective", "objective"), ("technology", "technology")),
        Array.Empty<string>());

    /// <summary>The assessment run dialog: comments only; it shows no analyst and no answers.</summary>
    public static AgentHighlightScreen AssessmentRunDialog { get; } = new("assessment_run_dialog", AgentItemKinds.AssessmentRun,
        Map(("comments", "Comments")), new[] { "analyst", "answer" });

    public static IReadOnlyList<AgentHighlightScreen> All { get; } =
        new[] { Risks, Incidents, Findings, Hosts, EntityForm, AssessmentRunDialog };

    /// <summary>The screen a watcher of <paramref name="kind"/> highlights on (the list screens and the entity form).</summary>
    public static AgentHighlightScreen? ForKind(string kind) => kind switch
    {
        AgentItemKinds.Risk => Risks,
        AgentItemKinds.Incident => Incidents,
        AgentItemKinds.Finding => Findings,
        AgentItemKinds.Host => Hosts,
        AgentItemKinds.Entity => EntityForm,
        AgentItemKinds.AssessmentRun => AssessmentRunDialog,
        _ => null
    };
}
