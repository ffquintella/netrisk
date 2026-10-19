using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>
/// "Qualidade dos dados — detalhe" (S40 §5.12.2; T256): the collapsible table under the always-visible strip — every item
/// with its value, its threshold and its "›", plus the counts the strip leaves out and the M40 evidence confidence, which
/// never enters quality (S39 §8.5). Fed by <see cref="RiskQualityStripViewModel"/>'s read; retrying reads again.
/// </summary>
public class RiskQualityPanelViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskQualityPanelTitle"];
    public string StrColItem { get; } = Localizer["RiskQualityPanelColItem"];
    public string StrColValue { get; } = Localizer["RiskQualityPanelColValue"];
    public string StrColThreshold { get; } = Localizer["RiskQualityPanelColThreshold"];
    public string StrNote { get; } = Localizer["RiskQualityPanelNote"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];

    #endregion

    public RiskQualityPanelViewModel(Func<Task> retry)
    {
        BtRetryClicked = ReactiveCommand.CreateFromTask(retry);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    #region PROPERTIES

    private bool _isActive;
    public bool IsActive { get => _isActive; private set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    private bool _isExpanded;
    /// <summary>Closed by default (§5.0: "▸ Qualidade dos dados — detalhe"); the strip above is the always-visible part.</summary>
    public bool IsExpanded { get => _isExpanded; set => this.RaiseAndSetIfChanged(ref _isExpanded, value); }

    public ObservableCollection<RiskQualityItemViewModel> Rows { get; } = [];

    private bool _hasRows;
    public bool HasRows { get => _hasRows; private set => this.RaiseAndSetIfChanged(ref _hasRows, value); }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    #endregion

    internal void Clear()
    {
        IsActive = false;
        Rows.Clear();
        HasRows = false;
        ErrorMessage = null;
        IsBusy = false;
    }

    internal void Activate() => IsActive = true;

    internal void Apply(IReadOnlyList<RiskQualityItemViewModel> rows)
    {
        Rows.Clear();
        foreach (var row in rows) Rows.Add(row);
        HasRows = Rows.Count > 0;
    }
}
