using DAL.Entities;
using DAL.Enums;
using Model.Integrations;

namespace ServerServices.Integrations.EntityMappings;

/// <summary>A rule as the sync and the preview read it.</summary>
public sealed record MappingRule(int Id, VendorMatchKind Kind, string Value, int EntityId, int Priority, RestampPolicy Policy);

/// <summary>What a winning rule does to one host.</summary>
public enum MappingOutcome
{
    /// <summary>The host had no entity: the rule attributes it.</summary>
    Assign = 1,

    /// <summary>A <see cref="RestampPolicy.FollowMapping"/> rule moves the host from another entity.</summary>
    Reassign = 2,

    /// <summary>The host keeps its entity: already the rule's, or another under <see cref="RestampPolicy.OnlyUnassigned"/>.</summary>
    Keep = 3
}

/// <summary>
/// The application rules of S40 §6.6 (T263) as pure functions, shared by the Vision One sync and the preview so the two
/// cannot disagree: the enabled rule of lowest priority (then lowest id) that matches one of the host's tags wins; an
/// asset-group rule never attributes a device (the API does not return group membership per device); the restamp policy
/// decides whether a host that already has an entity moves.
/// </summary>
public static class VendorEntityMappingRules
{
    /// <summary>The kinds that attribute a host. <see cref="VendorMatchKind.V1AssetGroup"/> is a reference indicator only.</summary>
    public static bool Attributes(VendorMatchKind kind) => kind is VendorMatchKind.V1CustomTag or VendorMatchKind.TenableTag;

    /// <summary>The rules in the order they are tried.</summary>
    public static IReadOnlyList<MappingRule> Order(IEnumerable<MappingRule> rules) =>
        rules.Where(r => Attributes(r.Kind)).OrderBy(r => r.Priority).ThenBy(r => r.Id).ToList();

    /// <summary>The first rule of <paramref name="ordered"/> whose value is one of <paramref name="tags"/>, or null.</summary>
    public static MappingRule? Winner(IReadOnlyList<MappingRule> ordered, IReadOnlyCollection<string> tags)
    {
        if (tags.Count == 0) return null;
        foreach (var rule in ordered)
            if (Attributes(rule.Kind) && tags.Any(t => VendorTagText.Same(t, rule.Value)))
                return rule;
        return null;
    }

    /// <summary>Whether <paramref name="rule"/> matches one of <paramref name="tags"/>, whatever the other rules say.</summary>
    public static bool Matches(MappingRule rule, IReadOnlyCollection<string> tags) =>
        Attributes(rule.Kind) && tags.Any(t => VendorTagText.Same(t, rule.Value));

    /// <summary>
    /// What the winning rule does to a host whose entity is <paramref name="current"/>, written by
    /// <paramref name="currentSource"/>: a <c>FollowMapping</c> rule moves it only over a writer it outranks (M58, T282,
    /// S39 D-20), so a manual or CMDB assignment stays.
    /// </summary>
    public static MappingOutcome Decide(int? current, MappingRule winner, string? currentSource = null) =>
        current == winner.EntityId ? MappingOutcome.Keep
        : current is null ? MappingOutcome.Assign
        : winner.Policy == RestampPolicy.FollowMapping && EntitySources.MayOverwrite(currentSource, EntitySources.Rule)
            ? MappingOutcome.Reassign
        : MappingOutcome.Keep;

    /// <summary>The entity a host ends with: the rule's on <see cref="MappingOutcome.Assign"/> or <see cref="MappingOutcome.Reassign"/>.</summary>
    public static int? Apply(int? current, MappingRule winner, string? currentSource = null) =>
        Decide(current, winner, currentSource) == MappingOutcome.Keep ? current : winner.EntityId;

    /// <summary>Applies the winning rule to <paramref name="host"/>, recording the rule as the writer when it moved it.</summary>
    public static MappingOutcome ApplyTo(Host host, MappingRule winner)
    {
        var outcome = Decide(host.EntityId, winner, host.EntitySource);
        if (outcome == MappingOutcome.Keep) return outcome;

        host.EntityId = winner.EntityId;
        host.EntitySource = EntitySources.Rule;
        return outcome;
    }
}

/// <summary>
/// A connection's target entity applied to a host it matched (M58, T282, S39 D-20), for the Vision One inventory and the
/// Jira Assets import. A host with no entity takes the target; one elsewhere moves only under
/// <see cref="RestampPolicy.FollowMapping"/> and only over a writer the connection outranks (<see cref="EntitySources"/>),
/// so a manual assignment is never overwritten. A connection without a target leaves the host alone. Policy:
/// docs/features/host-entity-assignment.md.
/// </summary>
public static class HostEntityRestamp
{
    public static MappingOutcome Decide(int? current, string? currentSource, int? target, RestampPolicy policy,
        string writer)
    {
        if (target is null || current == target) return MappingOutcome.Keep;
        if (current is null) return MappingOutcome.Assign;

        return policy == RestampPolicy.FollowMapping && EntitySources.MayOverwrite(currentSource, writer)
            ? MappingOutcome.Reassign
            : MappingOutcome.Keep;
    }

    /// <summary>Applies <see cref="Decide"/> to <paramref name="host"/>, recording <paramref name="writer"/> when it moved it.</summary>
    public static MappingOutcome ApplyTo(Host host, int? target, RestampPolicy policy, string writer)
    {
        var outcome = Decide(host.EntityId, host.EntitySource, target, policy, writer);
        if (outcome == MappingOutcome.Keep) return outcome;

        host.EntityId = target;
        host.EntitySource = writer;
        return outcome;
    }
}
