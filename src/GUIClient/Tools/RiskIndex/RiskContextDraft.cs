using System;
using System.Collections.Generic;
using System.Linq;
using Model.Entities;

namespace GUIClient.Tools.RiskIndex;

/// <summary>The three editable values of one entity's risk context.</summary>
public readonly record struct RiskContextValues(int? Criticality, bool? InternetFacing, int? SecurityClassificationId);

/// <summary>
/// The Risk context tab's pending edits (S40 §6.7): the values as loaded and as edited per entity, which rows are changed,
/// and the one all-or-nothing batch the Save sends. A value for a field the entity's type does not support is never sent
/// (the API refuses it with <c>field_not_supported_for_type</c>). Pure, tested in <c>GUIClient.Tests</c>.
/// </summary>
public sealed class RiskContextDraft
{
    private readonly Dictionary<int, EntityRiskContextRowDto> _rows = new();
    private readonly Dictionary<int, RiskContextValues> _edits = new();

    public void Load(IEnumerable<EntityRiskContextRowDto> rows)
    {
        _rows.Clear();
        _edits.Clear();
        foreach (var row in rows) _rows[row.EntityId] = row;
    }

    public static RiskContextValues Stored(EntityRiskContextRowDto row) =>
        new(row.Criticality, row.InternetFacing, row.SecurityClassificationId);

    public RiskContextValues Current(int entityId) =>
        _edits.TryGetValue(entityId, out var edited) ? edited : Stored(_rows[entityId]);

    /// <summary>Records an edit; an edit back to the stored values is no longer a change.</summary>
    public void Set(int entityId, RiskContextValues values)
    {
        if (!_rows.TryGetValue(entityId, out var row)) throw new ArgumentOutOfRangeException(nameof(entityId));
        var clean = new RiskContextValues(
            row.SupportsCriticality ? values.Criticality : null,
            row.SupportsInternetFacing ? values.InternetFacing : null,
            row.SupportsSecurityClassification ? values.SecurityClassificationId : null);
        if (clean == Stored(row)) _edits.Remove(entityId);
        else _edits[entityId] = clean;
    }

    public bool IsChanged(int entityId) => _edits.ContainsKey(entityId);

    public bool IsDirty => _edits.Count > 0;

    public int ChangedCount => _edits.Count;

    public void Discard() => _edits.Clear();

    /// <summary>"Name: field old → new" per changed field, for the confirmation of the batch.</summary>
    public IReadOnlyList<(EntityRiskContextRowDto Row, RiskContextValues Before, RiskContextValues After)> Changes() =>
        _edits.OrderBy(e => _rows[e.Key].Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => (_rows[e.Key], Stored(_rows[e.Key]), e.Value)).ToList();

    /// <summary>The batch request, or null with the reason key when it cannot be sent yet.</summary>
    public (EntityRiskContextBatchRequest? Request, string? ReasonKey) Batch(string? justification)
    {
        if (!IsDirty) return (null, "RiskAdminNoChanges");
        var text = justification?.Trim() ?? "";
        if (text.Length < EntityRiskContextRules.MinimumJustificationLength) return (null, "RiskContextJustificationTooShort");
        if (text.Length > EntityRiskContextRules.MaximumJustificationLength) return (null, "RiskContextJustificationTooLong");
        if (_edits.Count > EntityRiskContextRules.MaximumBatchSize) return (null, "RiskContextBatchTooLarge");

        var changes = _edits.OrderBy(e => e.Key)
            .Select(e => new EntityRiskContextChange(e.Key, e.Value.Criticality, e.Value.InternetFacing,
                e.Value.SecurityClassificationId))
            .ToList();
        return (new EntityRiskContextBatchRequest(text, changes), null);
    }
}
