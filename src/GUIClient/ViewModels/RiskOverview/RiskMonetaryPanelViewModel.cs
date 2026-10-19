using ReactiveUI;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>
/// "Painel monetário — fora do índice" (S40 §5.10; T256), interim mode. The server has no monetary read yet
/// (<c>GET /RiskIndex/Monetary</c>, T249 note) and the residual mean needs T290, so every line shows the spec's unavailable
/// state — never a figure the server did not supply, and never a bare total.
/// </summary>
public class RiskMonetaryPanelViewModel : ViewModelBase
{
    public string StrTitle { get; } = Localizer["RiskMonetaryTitle"];
    public string StrResidualLabel { get; } = Localizer["RiskMonetary_ResidualLabel"];
    public string StrResidualState { get; } = Localizer["RiskMonetary_ResidualUnavailable"];
    public string StrInherentLabel { get; } = Localizer["RiskMonetary_InherentLabel"];
    public string StrInherentState { get; } = Localizer["RiskMonetary_InherentUnavailable"];
    public string StrP95Label { get; } = Localizer["RiskMonetary_P95Label"];
    public string StrP95State { get; } = Localizer["RiskMonetary_P95Unavailable"];
    public string StrNote { get; } = Localizer["RiskMonetaryNote"];

    private bool _isActive;
    /// <summary>A node is shown ("Meu escopo" and the §5.13 states have none).</summary>
    public bool IsActive { get => _isActive; set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    private bool _isExpanded;
    /// <summary>Closed by default (§5.0: "▸ Painel monetário").</summary>
    public bool IsExpanded { get => _isExpanded; set => this.RaiseAndSetIfChanged(ref _isExpanded, value); }
}
