using System;
using System.Collections.Generic;

namespace GUIClient.ViewModels.Dialogs;

/// <summary>
/// Maps a dialog view-model name onto the view class names that may implement it.
///
/// <see cref="DialogService"/> resolves views by convention, and the convention used to be a
/// bare <c>Replace("ViewModel", "")</c>. That only ever matched views whose name is the view
/// model's name minus the suffix — i.e. the <c>*Dialog</c> ones, because they pair with
/// <c>*DialogViewModel</c>. Every view named <c>*Window</c> (<c>EditRiskWindow</c>,
/// <c>CloseRiskWindow</c>, <c>EditMitigationWindow</c>, <c>EditIncidentWindow</c>,
/// <c>IncidentResponsePlanWindow</c>, <c>IncidentResponsePlanTaskWindow</c>,
/// <c>VulnerabilityImportWindow</c>) pairs with a view model that has no <c>Window</c> in its
/// name, so it was unreachable and opening it threw "View for … was not found!".
///
/// This type therefore returns every plausible view name rather than one, and lives free of
/// Avalonia references so <c>GUIClient.Tests</c> can compile it into a headless run and assert
/// the convention holds for every dialog the app actually opens.
/// </summary>
public static class DialogViewNaming
{
    private const string ViewModelSuffix = "ViewModel";

    /// <summary>
    /// The view class names that could implement <paramref name="viewModelName"/>, in the order
    /// they should be tried: the bare stem first (so existing <c>*Dialog</c> pairings keep
    /// resolving exactly as before), then the <c>Window</c> and <c>Dialog</c> suffixed forms.
    /// </summary>
    /// <param name="viewModelName">
    /// A dialog view model's type name, e.g. <c>EditRiskViewModel</c>. The <c>ViewModel</c>
    /// suffix is optional.
    /// </param>
    /// <returns><c>EditRiskViewModel</c> → <c>EditRisk</c>, <c>EditRiskWindow</c>, <c>EditRiskDialog</c>.</returns>
    public static IReadOnlyList<string> GetCandidateViewNames(string viewModelName)
    {
        if (string.IsNullOrWhiteSpace(viewModelName))
        {
            throw new ArgumentException("A dialog view model name is required.", nameof(viewModelName));
        }

        // Trim only a trailing suffix. The old Replace() stripped the substring wherever it
        // appeared, which would mangle any name containing "ViewModel" mid-word.
        var stem = viewModelName.EndsWith(ViewModelSuffix, StringComparison.Ordinal)
            ? viewModelName[..^ViewModelSuffix.Length]
            : viewModelName;

        if (stem.Length == 0)
        {
            throw new ArgumentException(
                $"'{viewModelName}' leaves no name behind once the '{ViewModelSuffix}' suffix is removed.",
                nameof(viewModelName));
        }

        return [stem, stem + "Window", stem + "Dialog"];
    }
}
