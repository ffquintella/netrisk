using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DAL.Enums;
using GUIClient.Interfaces;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>
/// The triage-lifecycle dialog (Track 3 milestone 3.2.1): pick the finding's new state and, where
/// the state demands one, state why.
///
/// The justification requirement is mirrored here from the server's rules rather than left to the
/// server to reject. The server still enforces it — that is what makes it a rule — but a form that
/// tells you the field is required before you submit is a better form.
/// </summary>
public class FindingStatusDialogViewModel
    : ParameterizedDialogViewModelBaseAsync<FindingStatusDialogResult, FindingStatusDialogParameter>, ISaveableDialog
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["ChangeStatus"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrJustification { get; } = Localizer["Justification"];
    public string StrCanonicalFinding { get; } = Localizer["CanonicalFinding"];
    public new string StrSave { get; } = Localizer["Save"];
    public new string StrCancel { get; } = Localizer["Cancel"];

    #endregion

    #region PROPERTIES

    private string _findingTitle = "";
    public string FindingTitle
    {
        get => _findingTitle;
        set => this.RaiseAndSetIfChanged(ref _findingTitle, value);
    }

    /// <summary>Only the states the server says this finding may move to.</summary>
    public ObservableCollection<FindingStatus> AllowedStatuses { get; } = new();

    private FindingStatus? _selectedStatus;
    public FindingStatus? SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedStatus, value);
            this.RaisePropertyChanged(nameof(JustificationRequired));
            this.RaisePropertyChanged(nameof(DuplicateRequired));
            this.RaisePropertyChanged(nameof(SaveEnabled));
        }
    }

    private string _justification = "";
    public string Justification
    {
        get => _justification;
        set
        {
            this.RaiseAndSetIfChanged(ref _justification, value);
            this.RaisePropertyChanged(nameof(SaveEnabled));
        }
    }

    private string _duplicateOfId = "";
    public string DuplicateOfId
    {
        get => _duplicateOfId;
        set
        {
            this.RaiseAndSetIfChanged(ref _duplicateOfId, value);
            this.RaisePropertyChanged(nameof(SaveEnabled));
        }
    }

    /// <summary>
    /// True for the suppressing states and for Duplicate — the same set the server's
    /// <c>FindingStatusExtensions.RequiresJustification</c> defines, read from the shared enum
    /// helpers so the two cannot drift.
    /// </summary>
    public bool JustificationRequired => SelectedStatus != null && SelectedStatus.Value.RequiresJustification();

    public bool DuplicateRequired => SelectedStatus == FindingStatus.Duplicate;

    /// <summary>
    /// Save is enabled only once the form carries everything the chosen state needs. The checks
    /// mirror the server's; the server is still the enforcer.
    /// </summary>
    public bool SaveEnabled
    {
        get
        {
            if (SelectedStatus == null) return false;
            if (JustificationRequired && string.IsNullOrWhiteSpace(Justification)) return false;
            if (DuplicateRequired && !int.TryParse(DuplicateOfId, out var id)) return false;
            if (DuplicateRequired && int.TryParse(DuplicateOfId, out var canonical) && canonical == _findingId)
                return false;

            return true;
        }
    }

    #endregion

    private int _findingId;

    public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

    /// <summary>Ctrl/Cmd+S accelerator target (see <see cref="ISaveableDialog"/>).</summary>
    public ICommand? SaveCommand => BtSaveClicked;

    public FindingStatusDialogViewModel()
    {
        BtSaveClicked = ReactiveCommand.Create(ExecuteSave);
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);
    }

    private void ExecuteSave()
    {
        Close(new FindingStatusDialogResult
        {
            Action = ResultActions.Ok,
            Status = SelectedStatus!.Value,
            Justification = string.IsNullOrWhiteSpace(Justification) ? null : Justification,
            DuplicateOfId = int.TryParse(DuplicateOfId, out var id) ? id : null
        });
    }

    private void ExecuteCancel() =>
        Close(new FindingStatusDialogResult { Action = ResultActions.Cancel });

    public override Task ActivateAsync(FindingStatusDialogParameter parameter,
        CancellationToken cancellationToken = default)
    {
        _findingId = parameter.FindingId;
        FindingTitle = parameter.FindingTitle ?? "";

        AllowedStatuses.Clear();
        foreach (var status in parameter.AllowedStatuses) AllowedStatuses.Add(status);

        // Left unset rather than defaulted to the first entry: changing a finding's triage state is
        // a decision, and pre-selecting one invites an accidental confirmation.
        SelectedStatus = null;

        return Task.CompletedTask;
    }
}
