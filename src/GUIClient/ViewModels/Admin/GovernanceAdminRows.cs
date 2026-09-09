using DAL.Entities;
using GUIClient.Tools;
using Model.Governance;

namespace GUIClient.ViewModels.Admin;

/// <summary>
/// One row of the risk-appetite grid (Track 8 milestone 8.3.3).
///
/// A display model rather than the entity itself, for one reason: the scope cell has to read
/// "Global" for the organization-wide row, and that word is localized. A grid column binds against
/// the row, not against the view model, so a binding on a bare <see cref="RiskAppetite"/> cannot
/// reach the localizer — it can only reach <c>EntityId</c>, which is how the column came to show a
/// raw numeric id and nothing at all for the global row.
/// </summary>
public sealed class AppetiteRow(RiskAppetite appetite, string globalLabel)
{
    /// <summary>The row the editor and the save/delete commands act on.</summary>
    public RiskAppetite Appetite { get; } = appetite;

    /// <summary>The scope, written for a human: the entity's name, "Global", or <c>#id</c>.</summary>
    public string EntityDisplay { get; } =
        EntityScopeLabel.Describe(appetite.EntityId, appetite.Entity?.DisplayName, globalLabel);

    public int Id => Appetite.Id;

    public int? EntityId => Appetite.EntityId;

    public double MaxAcceptableResidual => Appetite.MaxAcceptableResidual;

    public double DualApprovalThreshold => Appetite.DualApprovalThreshold;

    public string? Notes => Appetite.Notes;
}

/// <summary>
/// One row of the "risks above appetite" grid (also 8.3.3).
///
/// The server fills <see cref="AppetiteBreachCount.EntityName"/> from the entity property bag and
/// leaves it null for the organization-wide bucket and for any entity with no name row, so the grid
/// bound straight at it showed an empty scope cell in both cases — and an empty cell beside a count
/// does not say which scope the count is for.
/// </summary>
public sealed class AppetiteBreachRow(AppetiteBreachCount breach, string globalLabel)
{
    public string EntityDisplay { get; } =
        EntityScopeLabel.Describe(breach.EntityId, breach.EntityName, globalLabel);

    public int Count { get; } = breach.Count;
}
