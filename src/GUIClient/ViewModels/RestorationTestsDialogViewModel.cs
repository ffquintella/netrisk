using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Continuity;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>A test outcome as the outcome picker shows it.</summary>
public sealed class RestorationOutcomeOption(RestorationTestOutcome outcome, string name)
{
    public RestorationTestOutcome Outcome { get; } = outcome;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

/// <summary>
/// The restoration tests of a process or IT service (Stage 9.3, S43 §7): the list, voided ones included
/// and marked, a form to record a new test and a void with a reason. Records are evidence: nothing here
/// edits or deletes one. Recording and voiding need <c>restoration_test_record</c> and global scope, which
/// the server reports and decides.
/// </summary>
public class RestorationTestsDialogViewModel
    : ParameterizedDialogViewModelBaseAsync<ContinuityDialogResult, ContinuityDialogParameter>
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RestorationTestsTitle"];
    public string StrTestedAt { get; } = Localizer["TestedAt"];
    public string StrOutcome { get; } = Localizer["TestOutcome"];
    public string StrAchievedRto { get; } = Localizer["AchievedRto"];
    public string StrAchievedRpo { get; } = Localizer["AchievedRpo"];
    public string StrDeclaredAtRecord { get; } = Localizer["DeclaredAtRecord"];
    public string StrEvidenceReference { get; } = Localizer["EvidenceReference"];
    public string StrRecordedBy { get; } = Localizer["RecordedBy"];
    public string StrVoided { get; } = Localizer["Voided"];
    public string StrRecordTest { get; } = Localizer["RecordTest"];
    public string StrVoidTest { get; } = Localizer["VoidTest"];
    public string StrNotes { get; } = Localizer["Notes"];
    public string StrDurationUnit { get; } = Localizer["DurationUnit"];

    private string StrAbsent { get; } = Localizer["BiaAbsent"];
    private string StrVoidedFormat { get; } = Localizer["VoidedLine"];
    private string StrVoidReason { get; } = Localizer["VoidReason"];
    private string StrVoidReasonHint { get; } = Localizer["VoidReasonHint"];
    private string StrNothingMeasured { get; } = Localizer["NothingMeasured"];
    private string StrTestedAtInFuture { get; } = Localizer["TestedAtInFuture"];
    private string StrInvalidDuration { get; } = Localizer["InvalidDuration"];

    #endregion

    #region SERVICES

    private IContinuityService ContinuityService { get; } = GetService<IContinuityService>();
    private IDialogService DialogService { get; } = GetService<IDialogService>();

    #endregion

    #region PROPERTIES

    private int _entityId;
    private bool _changed;

    private string _entityName = string.Empty;
    public string EntityName { get => _entityName; set => this.RaiseAndSetIfChanged(ref _entityName, value); }

    private bool _canRecordTests;
    public bool CanRecordTests { get => _canRecordTests; private set => this.RaiseAndSetIfChanged(ref _canRecordTests, value); }

    public ObservableCollection<RestorationTestRow> Tests { get; } = [];

    private RestorationTestRow? _selectedTest;
    public RestorationTestRow? SelectedTest
    {
        get => _selectedTest;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTest, value);
            this.RaisePropertyChanged(nameof(IsVoidEnabled));
        }
    }

    public List<RestorationOutcomeOption> Outcomes { get; }

    private RestorationOutcomeOption _outcome;
    public RestorationOutcomeOption Outcome { get => _outcome; set { if (value is null) return; this.RaiseAndSetIfChanged(ref _outcome, value); Revalidate(); } }

    public List<DurationUnitOption> Units { get; }

    private DateTimeOffset? _testedOn = DateTimeOffset.Now.Date;
    public DateTimeOffset? TestedOn { get => _testedOn; set { this.RaiseAndSetIfChanged(ref _testedOn, value); Revalidate(); } }

    private TimeSpan? _testedTime = DateTime.Now.TimeOfDay;
    public TimeSpan? TestedTime { get => _testedTime; set { this.RaiseAndSetIfChanged(ref _testedTime, value); Revalidate(); } }

    private decimal? _achievedRto;
    public decimal? AchievedRto { get => _achievedRto; set { this.RaiseAndSetIfChanged(ref _achievedRto, value); Revalidate(); } }

    private DurationUnitOption _achievedRtoUnit;
    public DurationUnitOption AchievedRtoUnit { get => _achievedRtoUnit; set { if (value is null) return; this.RaiseAndSetIfChanged(ref _achievedRtoUnit, value); Revalidate(); } }

    private decimal? _achievedRpo;
    public decimal? AchievedRpo { get => _achievedRpo; set { this.RaiseAndSetIfChanged(ref _achievedRpo, value); Revalidate(); } }

    private DurationUnitOption _achievedRpoUnit;
    public DurationUnitOption AchievedRpoUnit { get => _achievedRpoUnit; set { if (value is null) return; this.RaiseAndSetIfChanged(ref _achievedRpoUnit, value); Revalidate(); } }

    private string? _evidence;
    public string? Evidence { get => _evidence; set => this.RaiseAndSetIfChanged(ref _evidence, value); }

    private string? _notes;
    public string? Notes { get => _notes; set => this.RaiseAndSetIfChanged(ref _notes, value); }

    private string _errorText = string.Empty;
    public string ErrorText { get => _errorText; private set => this.RaiseAndSetIfChanged(ref _errorText, value); }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    private bool _isRecordEnabled;
    public bool IsRecordEnabled { get => _isRecordEnabled; private set => this.RaiseAndSetIfChanged(ref _isRecordEnabled, value); }

    public bool IsVoidEnabled => CanRecordTests && SelectedTest is { IsVoided: false };

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtRecordTestClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtVoidTestClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCloseClicked { get; }

    #endregion

    public RestorationTestsDialogViewModel()
    {
        Outcomes = Enum.GetValues<RestorationTestOutcome>()
            .Select(o => new RestorationOutcomeOption(o, Localizer[ContinuitySummary.OutcomeKey(o)]))
            .ToList();
        Units = Enum.GetValues<DurationUnit>()
            .Select(u => new DurationUnitOption(u, Localizer[ContinuitySummary.UnitKey(u)]))
            .ToList();

        _outcome = Outcomes[0];
        _achievedRtoUnit = Units.Single(u => u.Unit == DurationUnit.Minutes);
        _achievedRpoUnit = Units.Single(u => u.Unit == DurationUnit.Minutes);

        BtRecordTestClicked = ReactiveCommand.CreateFromTask(RecordAsync);
        BtVoidTestClicked = ReactiveCommand.CreateFromTask(VoidAsync);
        BtCloseClicked = ReactiveCommand.Create(() => Close(new ContinuityDialogResult
        {
            Action = _changed ? ResultActions.Ok : ResultActions.Cancel,
            Changed = _changed
        }));
    }

    public override async Task ActivateAsync(ContinuityDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        _entityId = parameter.EntityId;
        EntityName = parameter.EntityName;
        CanRecordTests = parameter.CanRecordTests;
        this.RaisePropertyChanged(nameof(IsVoidEnabled));
        Revalidate();

        try
        {
            await WithBusyAsync(ReloadAsync);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error loading the restoration tests of entity {Id}", _entityId);
            Toasts.Error(ExplainError(ex));
        }
    }

    private async Task ReloadAsync()
    {
        var tests = await ContinuityService.GetRestorationTestsAsync(_entityId);

        Tests.Clear();
        foreach (var test in tests)
            Tests.Add(new RestorationTestRow(test, Localizer[ContinuitySummary.OutcomeKey(test.Outcome)], StrAbsent,
                StrVoidedFormat));

        SelectedTest = null;
    }

    private DateTime? TestedAtUtc() =>
        TestedOn is { } day ? DateTime.SpecifyKind(day.Date + (TestedTime ?? TimeSpan.Zero), DateTimeKind.Local).ToUniversalTime() : null;

    private void Revalidate()
    {
        if (Units is null || _achievedRtoUnit is null || _achievedRpoUnit is null || _outcome is null) return;

        var valid = ContinuitySummary.TryToMinutes(AchievedRto, AchievedRtoUnit.Unit, out var rto)
                    & ContinuitySummary.TryToMinutes(AchievedRpo, AchievedRpoUnit.Unit, out var rpo);

        ErrorText = !valid ? StrInvalidDuration
            : TestedAtUtc() is not { } at || at > DateTime.UtcNow ? StrTestedAtInFuture
            : Outcome.Outcome == RestorationTestOutcome.Succeeded && rto is null && rpo is null ? StrNothingMeasured
            : string.Empty;

        this.RaisePropertyChanged(nameof(HasError));
        IsRecordEnabled = CanRecordTests && string.IsNullOrEmpty(ErrorText);
    }

    private async Task RecordAsync()
    {
        if (!IsRecordEnabled || TestedAtUtc() is not { } testedAt) return;

        ContinuitySummary.TryToMinutes(AchievedRto, AchievedRtoUnit.Unit, out var rto);
        ContinuitySummary.TryToMinutes(AchievedRpo, AchievedRpoUnit.Unit, out var rpo);

        var request = new RestorationTestCreateRequest
        {
            TestedAt = testedAt,
            Outcome = Outcome.Outcome,
            AchievedRtoMinutes = rto,
            AchievedRpoMinutes = rpo,
            EvidenceReference = Evidence,
            Notes = Notes
        };

        await RunAsync(Localizer["RestorationTestRecordedMSG"], async () =>
        {
            await ContinuityService.RecordRestorationTestAsync(_entityId, request);
            _changed = true;
            AchievedRto = null;
            AchievedRpo = null;
            Evidence = null;
            Notes = null;
            await ReloadAsync();
        });
    }

    private async Task VoidAsync()
    {
        if (!IsVoidEnabled || SelectedTest is null) return;

        var testId = SelectedTest.Test.Id;

        var dialog = await DialogService.ShowDialogAsync<StringDialogResult, StringDialogParameter>(
            nameof(EditSingleStringDialogViewModel),
            new StringDialogParameter { Title = StrVoidTest, FieldName = StrVoidReason, Value = string.Empty });

        if (dialog is not { Action: ResultActions.Ok } || string.IsNullOrWhiteSpace(dialog.Result)) return;

        if (dialog.Result.Trim().Length < 10)
        {
            Toasts.Error(StrVoidReasonHint);
            return;
        }

        await RunAsync(string.Empty, async () =>
        {
            await ContinuityService.VoidRestorationTestAsync(_entityId, testId, dialog.Result);
            _changed = true;
            await ReloadAsync();
        });
    }
}
