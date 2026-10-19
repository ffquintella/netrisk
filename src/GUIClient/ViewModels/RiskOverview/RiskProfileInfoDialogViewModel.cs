using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools.RiskIndex;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>A profile version in the dialog's list: "v2 · Equilibrado · Aposentado".</summary>
public sealed class RiskProfileVersionItem(RiskIndexProfileSummaryDto summary, string text)
{
    public RiskIndexProfileSummaryDto Summary { get; } = summary;
    public string Text { get; } = text;
    public override string ToString() => Text;
}

/// <summary>One parameter row: its group, its key and the value(s) compared.</summary>
public sealed record RiskProfileParameterRow(string Group, string Key, string Value, string? OtherValue);

/// <summary>
/// The read-only profile dialog behind the profile chip (S40 §5.1; T251): the snapshot's profile — version, status, mode,
/// approver, committee reference, validity and its parameters — and the other versions, each compared parameter by parameter
/// with it. It never edits; drafts and approval are the configuration screens' (M55).
/// </summary>
public class RiskProfileInfoDialogViewModel
    : ParameterizedDialogViewModelBaseAsync<DialogResultBase, RiskProfileInfoDialogParameter>
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskProfileInfoTitle"];
    public string StrCurrent { get; } = Localizer["RiskProfileInfoCurrent"];
    public string StrVersion { get; } = Localizer["RiskProfileVersion"];
    public string StrName { get; } = Localizer["RiskProfileName"];
    public string StrPreset { get; } = Localizer["RiskProfilePreset"];
    public string StrStatus { get; } = Localizer["RiskProfileStatus"];
    public string StrMode { get; } = Localizer["RiskProfileMode"];
    public string StrEffectiveFrom { get; } = Localizer["RiskProfileEffectiveFrom"];
    public string StrRetiredAt { get; } = Localizer["RiskProfileRetiredAt"];
    public string StrApprovedBy { get; } = Localizer["RiskProfileApprovedBy"];
    public string StrCommitteeRef { get; } = Localizer["RiskProfileCommitteeRef"];
    public string StrCreatedBy { get; } = Localizer["RiskProfileCreatedBy"];
    public string StrRebaselineNote { get; } = Localizer["RiskProfileRebaselineNote"];
    public string StrParameters { get; } = Localizer["RiskProfileParameters"];
    public string StrParameterGroup { get; } = Localizer["RiskProfileParameterGroup"];
    public string StrParameterKey { get; } = Localizer["RiskProfileParameterKey"];
    public string StrParameterValue { get; } = Localizer["RiskProfileParameterValue"];
    public string StrOtherVersions { get; } = Localizer["RiskProfileOtherVersions"];
    public string StrCompareHint { get; } = Localizer["RiskProfileCompareHint"];
    public string StrNoDifferences { get; } = Localizer["RiskProfileNoDifferences"];
    public string StrDiffSelected { get; } = Localizer["RiskProfileDiffSelected"];
    public string StrDiffThis { get; } = Localizer["RiskProfileDiffThis"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    #endregion

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    private RiskIndexProfileDto? _profile;

    public RiskProfileInfoDialogViewModel()
    {
        BtCloseClicked = ReactiveCommand.Create(() => Close());
        this.WhenAnyValue(vm => vm.SelectedVersion).Subscribe(item => _ = CompareAsync(item));
    }

    public ReactiveCommand<RxVoid, RxVoid> BtCloseClicked { get; }

    #region PROPERTIES

    private string _versionText = "—";
    public string VersionText { get => _versionText; private set => this.RaiseAndSetIfChanged(ref _versionText, value); }

    private string _nameText = "—";
    public string NameText { get => _nameText; private set => this.RaiseAndSetIfChanged(ref _nameText, value); }

    private string _presetText = "—";
    public string PresetText { get => _presetText; private set => this.RaiseAndSetIfChanged(ref _presetText, value); }

    private string _statusText = "—";
    public string StatusText { get => _statusText; private set => this.RaiseAndSetIfChanged(ref _statusText, value); }

    private string _modeText = "—";
    public string ModeText { get => _modeText; private set => this.RaiseAndSetIfChanged(ref _modeText, value); }

    private string _effectiveFromText = "—";
    public string EffectiveFromText { get => _effectiveFromText; private set => this.RaiseAndSetIfChanged(ref _effectiveFromText, value); }

    private string _retiredAtText = "—";
    public string RetiredAtText { get => _retiredAtText; private set => this.RaiseAndSetIfChanged(ref _retiredAtText, value); }

    private string _approvedByText = "—";
    public string ApprovedByText { get => _approvedByText; private set => this.RaiseAndSetIfChanged(ref _approvedByText, value); }

    private string _committeeRefText = "—";
    public string CommitteeRefText { get => _committeeRefText; private set => this.RaiseAndSetIfChanged(ref _committeeRefText, value); }

    private string _createdByText = "—";
    public string CreatedByText { get => _createdByText; private set => this.RaiseAndSetIfChanged(ref _createdByText, value); }

    private bool _isRebaseline;
    public bool IsRebaseline { get => _isRebaseline; private set => this.RaiseAndSetIfChanged(ref _isRebaseline, value); }

    public ObservableCollection<RiskProfileParameterRow> Parameters { get; } = [];

    public ObservableCollection<RiskProfileVersionItem> OtherVersions { get; } = [];

    public ObservableCollection<RiskProfileParameterRow> Differences { get; } = [];

    private RiskProfileVersionItem? _selectedVersion;
    public RiskProfileVersionItem? SelectedVersion
    {
        get => _selectedVersion;
        set => this.RaiseAndSetIfChanged(ref _selectedVersion, value);
    }

    private string? _diffTitle;
    public string? DiffTitle { get => _diffTitle; private set => this.RaiseAndSetIfChanged(ref _diffTitle, value); }

    private bool _hasNoDifferences;
    public bool HasNoDifferences { get => _hasNoDifferences; private set => this.RaiseAndSetIfChanged(ref _hasNoDifferences, value); }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    #endregion

    public override async Task ActivateAsync(RiskProfileInfoDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        await WithBusyAsync(async () =>
        {
            try
            {
                var culture = CultureInfo.CurrentCulture;
                _profile = await RiskIndexService.GetProfileAsync(parameter.ProfileId, cancellationToken);
                var profile = _profile;

                VersionText = "v" + profile.Version.ToString(culture);
                NameText = Dash(profile.Name);
                PresetText = Dash(profile.Preset);
                StatusText = Localizer[RiskOverviewTopBar.ProfileStatusKey(profile.Status)];
                ModeText = Localizer[RiskOverviewTopBar.ModeKey(profile.Mode)];
                EffectiveFromText = Day(profile.EffectiveFrom, culture);
                RetiredAtText = Day(profile.RetiredAt, culture);
                ApprovedByText = Dash(profile.ApprovedBy);
                CommitteeRefText = Dash(profile.CommitteeDecisionRef);
                CreatedByText = Dash(profile.CreatedBy);
                IsRebaseline = profile.IsRebaseline;

                // Diffing a profile with itself lists every parameter with its value, flattened the server's way.
                var all = await RiskIndexService.GetProfileDiffAsync(profile.Id, profile.Id, cancellationToken);
                Replace(Parameters, all.Changes.Select(c => new RiskProfileParameterRow(c.Group, c.Key, c.ValueA ?? "—", null)));

                var versions = await RiskIndexService.GetProfilesAsync(cancellationToken);
                Replace(OtherVersions, versions.Where(v => v.Id != profile.Id).OrderByDescending(v => v.Version)
                    .Select(v => new RiskProfileVersionItem(v, string.Format(culture, Localizer["RiskProfileListItemFMT"],
                        v.Version, Dash(v.Name), Localizer[RiskOverviewTopBar.ProfileStatusKey(v.Status)]))));
                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error reading risk index profile {Id}", parameter.ProfileId);
                ErrorMessage = Explain(ex);
            }
        });
    }

    /// <summary>The parameters that differ between the selected version and this one.</summary>
    private async Task CompareAsync(RiskProfileVersionItem? item)
    {
        Differences.Clear();
        HasNoDifferences = false;
        DiffTitle = null;
        if (item is null || _profile is null) return;

        try
        {
            var diff = await RiskIndexService.GetProfileDiffAsync(item.Summary.Id, _profile.Id);
            if (!ReferenceEquals(item, SelectedVersion)) return;

            DiffTitle = string.Format(CultureInfo.CurrentCulture, Localizer["RiskProfileDiffFMT"], item.Summary.Version,
                _profile.Version);
            Replace(Differences, diff.Changes.Where(c => c.Changed)
                .Select(c => new RiskProfileParameterRow(c.Group, c.Key, c.ValueA ?? "—", c.ValueB ?? "—")));
            HasNoDifferences = Differences.Count == 0;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error comparing risk index profiles {A} and {B}", item.Summary.Id, _profile.Id);
            ErrorMessage = Explain(ex);
        }
    }

    private string Explain(Exception ex) => ex switch
    {
        InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal =>
            RiskApiErrorText.Translate(code, refusal.Message, RiskHeadlineViewModel.Text, StrLoadError),
        DataNotFoundException => ExplainError(ex),
        _ => StrLoadError
    };

    private static string Dash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string Day(DateTime? value, CultureInfo culture) =>
        value is { } v ? v.ToLocalTime().ToString("d", culture) : "—";

    private static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
