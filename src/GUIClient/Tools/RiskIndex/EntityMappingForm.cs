using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using Model.Integrations;

namespace GUIClient.Tools.RiskIndex;

/// <summary>The rule being edited in the Sources tab's entity-mapping panel (S40 §6.6; T263); <see cref="Id"/> null = new.</summary>
public sealed record EntityMappingDraft(int? Id, VendorMatchKind Kind, string Value, int? EntityId, int Priority,
    RestampPolicy Policy, bool Enabled)
{
    /// <summary>Kind, value and entity: what the preview and the save need (S40 §6.6).</summary>
    public bool IsComplete => EntityId is not null && VendorTagText.Canonical(Value) is not null;

    public static EntityMappingDraft New(int priority) =>
        new(null, VendorMatchKind.V1CustomTag, "", null, priority, RestampPolicy.OnlyUnassigned, true);

    public static EntityMappingDraft From(VendorEntityMappingDto rule) =>
        new(rule.Id, rule.Kind, rule.Value, rule.EntityId, rule.Priority, rule.Policy, rule.Enabled);

    /// <summary>The request body; only for a complete draft.</summary>
    public VendorEntityMappingUpsert ToUpsert(int provider, int connectionId) =>
        new(provider, connectionId, Kind, VendorTagText.Canonical(Value) ?? "", null, EntityId ?? 0, Priority, Policy, Enabled);

    /// <summary>Whether the draft says what the stored rule says (a saved rule is not dirty).</summary>
    public bool SameAs(VendorEntityMappingDto? rule) =>
        rule is not null && Id == rule.Id && Kind == rule.Kind &&
        string.Equals(VendorTagText.Canonical(Value), rule.Value, StringComparison.Ordinal) && EntityId == rule.EntityId &&
        Priority == rule.Priority && Policy == rule.Policy && Enabled == rule.Enabled;
}

/// <summary>
/// The commands of the entity-mapping panel (S40 §6.6 table) as pure gates, each closed with the resource key its tooltip
/// shows. The API refuses whatever slips through — with <c>duplicate_mapping</c>, and <c>mapping_requires_global_scope</c>
/// for a scoped user, which the client cannot know beforehand.
/// </summary>
public static class EntityMappingGates
{
    public const string RequiresIntegrations = "EntityMappingRequiresConfiguration";
    public const string ChooseConnection = "EntityMappingChooseConnection";
    public const string Incomplete = "EntityMappingIncomplete";
    public const string Duplicate = "EntityMappingDuplicate";
    public const string SelectRule = "EntityMappingSelectRule";
    public const string NoChanges = "RiskAdminNoChanges";

    /// <summary>The highest priority a rule may take (the server's bound).</summary>
    public const int MaxPriority = 9999;

    public static Gate New(bool canWrite, int? connectionId) =>
        !canWrite ? Gate.Closed(RequiresIntegrations)
        : connectionId is null ? Gate.Closed(ChooseConnection)
        : Gate.Open;

    public static Gate Save(bool canWrite, EntityMappingDraft? draft, bool isDirty, IEnumerable<VendorEntityMappingDto> rules) =>
        !canWrite ? Gate.Closed(RequiresIntegrations)
        : draft is null ? Gate.Closed(SelectRule)
        : !isDirty ? Gate.Closed(NoChanges)
        : !draft.IsComplete ? Gate.Closed(Incomplete)
        : IsDuplicate(draft, rules) ? Gate.Closed(Duplicate)
        : Gate.Open;

    public static Gate Delete(bool canWrite, EntityMappingDraft? draft) =>
        !canWrite ? Gate.Closed(RequiresIntegrations)
        : draft?.Id is null ? Gate.Closed(SelectRule)
        : Gate.Open;

    /// <summary>The preview runs only for a complete rule; otherwise the card says what is missing.</summary>
    public static Gate Preview(EntityMappingDraft? draft) =>
        draft is { IsComplete: true } ? Gate.Open : Gate.Closed(Incomplete);

    /// <summary>Another rule of the connection with the same kind and value, compared as the sync compares tags.</summary>
    public static bool IsDuplicate(EntityMappingDraft draft, IEnumerable<VendorEntityMappingDto> rules) =>
        rules.Any(r => r.Id != draft.Id && r.Kind == draft.Kind && VendorTagText.Same(r.Value, draft.Value));

    /// <summary>A new rule goes last: one above the highest priority in use.</summary>
    public static int NextPriority(IEnumerable<VendorEntityMappingDto> rules)
    {
        var highest = rules.Select(r => r.Priority).DefaultIfEmpty(0).Max();
        return Math.Clamp(highest + 1, 1, MaxPriority);
    }
}
