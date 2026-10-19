using ReactiveUI;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>
/// The Top Risks space (S40 §5.11; T256): it exists from the interim mode on and holds only the placeholder — the list is
/// M43's (T172), with no approximation in the interim (S39 §1.2 item 5, §15.1).
/// </summary>
public class RiskTopRisksPlaceholderViewModel : ViewModelBase
{
    public string StrTitle { get; } = Localizer["RiskTopRisksTitle"];
    public string StrPlaceholder { get; } = Localizer["RiskTopRisksPlaceholder"];

    private bool _isActive;
    public bool IsActive { get => _isActive; set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    private bool _isExpanded;
    /// <summary>Closed by default (§5.0: "▸ Top Risks").</summary>
    public bool IsExpanded { get => _isExpanded; set => this.RaiseAndSetIfChanged(ref _isExpanded, value); }
}
