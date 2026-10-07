using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Continuity;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>A duration unit as the unit pickers show it.</summary>
public sealed class DurationUnitOption(DurationUnit unit, string name)
{
    public DurationUnit Unit { get; } = unit;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

/// <summary>
/// Declares or replaces the business impact analysis of a process or IT service (Stage 9.3, S43 §7):
/// MTPD/MAO, RTO and RPO, each a value and a unit, the analysis date and notes.
///
/// Each objective is optional, but one at least is needed, and the RTO cannot exceed the MTPD — the
/// form says so before sending, and the server checks both again. A refusal shows the server's own
/// sentence and keeps the dialog open.
/// </summary>
public class EditBiaDialogViewModel
    : ParameterizedDialogViewModelBaseAsync<ContinuityDialogResult, ContinuityDialogParameter>
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["EditBiaTitle"];
    public string StrMtpd { get; } = Localizer["Mtpd"];
    public string StrRto { get; } = Localizer["Rto"];
    public string StrRpo { get; } = Localizer["Rpo"];
    public string StrMtpdHint { get; } = Localizer["MtpdHint"];
    public string StrRtoHint { get; } = Localizer["RtoHint"];
    public string StrRpoHint { get; } = Localizer["RpoHint"];
    public string StrDurationUnit { get; } = Localizer["DurationUnit"];
    public string StrAssessedAt { get; } = Localizer["BiaAssessedAt"];
    public string StrNotes { get; } = Localizer["BiaNotes"];

    private string StrRtoExceedsMtpd { get; } = Localizer["RtoExceedsMtpd"];
    private string StrNothingDeclared { get; } = Localizer["NothingDeclared"];
    private string StrInvalidDuration { get; } = Localizer["InvalidDuration"];

    #endregion

    #region SERVICES

    private IContinuityService ContinuityService { get; } = GetService<IContinuityService>();

    #endregion

    #region PROPERTIES

    private int _entityId;

    private string _entityName = string.Empty;
    public string EntityName
    {
        get => _entityName;
        set => this.RaiseAndSetIfChanged(ref _entityName, value);
    }

    public List<DurationUnitOption> Units { get; }

    private decimal? _mtpdValue;
    public decimal? MtpdValue { get => _mtpdValue; set { this.RaiseAndSetIfChanged(ref _mtpdValue, value); Revalidate(); } }

    private DurationUnitOption _mtpdUnit;
    public DurationUnitOption MtpdUnit { get => _mtpdUnit; set { if (value is null) return; this.RaiseAndSetIfChanged(ref _mtpdUnit, value); Revalidate(); } }

    private decimal? _rtoValue;
    public decimal? RtoValue { get => _rtoValue; set { this.RaiseAndSetIfChanged(ref _rtoValue, value); Revalidate(); } }

    private DurationUnitOption _rtoUnit;
    public DurationUnitOption RtoUnit { get => _rtoUnit; set { if (value is null) return; this.RaiseAndSetIfChanged(ref _rtoUnit, value); Revalidate(); } }

    private decimal? _rpoValue;
    public decimal? RpoValue { get => _rpoValue; set { this.RaiseAndSetIfChanged(ref _rpoValue, value); Revalidate(); } }

    private DurationUnitOption _rpoUnit;
    public DurationUnitOption RpoUnit { get => _rpoUnit; set { if (value is null) return; this.RaiseAndSetIfChanged(ref _rpoUnit, value); Revalidate(); } }

    private DateTimeOffset? _assessedAt;
    public DateTimeOffset? AssessedAt { get => _assessedAt; set => this.RaiseAndSetIfChanged(ref _assessedAt, value); }

    private string? _notes;
    public string? Notes { get => _notes; set => this.RaiseAndSetIfChanged(ref _notes, value); }

    private string _errorText = string.Empty;
    public string ErrorText { get => _errorText; private set => this.RaiseAndSetIfChanged(ref _errorText, value); }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    private bool _isSaveEnabled;
    public bool IsSaveEnabled { get => _isSaveEnabled; private set => this.RaiseAndSetIfChanged(ref _isSaveEnabled, value); }

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

    #endregion

    public EditBiaDialogViewModel()
    {
        Units = Enum.GetValues<DurationUnit>()
            .Select(u => new DurationUnitOption(u, Localizer[ContinuitySummary.UnitKey(u)]))
            .ToList();

        var hours = Units.Single(u => u.Unit == DurationUnit.Hours);
        _mtpdUnit = hours;
        _rtoUnit = hours;
        _rpoUnit = hours;

        BtSaveClicked = ReactiveCommand.CreateFromTask(SaveAsync);
        BtCancelClicked = ReactiveCommand.Create(() => Close(new ContinuityDialogResult
            { Action = ResultActions.Cancel, Changed = false }));
    }

    public override Task ActivateAsync(ContinuityDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        _entityId = parameter.EntityId;
        EntityName = parameter.EntityName;

        if (parameter.Bia is { } bia)
        {
            (MtpdValue, MtpdUnit) = Start(bia.MtpdMinutes);
            (RtoValue, RtoUnit) = Start(bia.RtoMinutes);
            (RpoValue, RpoUnit) = Start(bia.RpoMinutes);
            AssessedAt = new DateTimeOffset(DateTime.SpecifyKind(bia.AssessedAt, DateTimeKind.Utc)).ToLocalTime();
            Notes = bia.Notes;
        }
        else
        {
            AssessedAt = DateTimeOffset.Now;
        }

        Revalidate();
        return Task.CompletedTask;
    }

    private (decimal?, DurationUnitOption) Start(int? minutes)
    {
        if (minutes is not { } value) return (null, Units.Single(u => u.Unit == DurationUnit.Hours));

        var (amount, unit) = ContinuitySummary.ForEditing(value);
        return (amount, Units.Single(u => u.Unit == unit));
    }

    /// <summary>The same rules the server applies, said before sending: valid durations, at least one
    /// objective, and an RTO within the MTPD.</summary>
    private void Revalidate()
    {
        // Called from setters during construction too, before the units exist.
        if (Units is null || _mtpdUnit is null || _rtoUnit is null || _rpoUnit is null) return;

        var valid = ContinuitySummary.TryToMinutes(MtpdValue, MtpdUnit.Unit, out var mtpd)
                    & ContinuitySummary.TryToMinutes(RtoValue, RtoUnit.Unit, out var rto)
                    & ContinuitySummary.TryToMinutes(RpoValue, RpoUnit.Unit, out var rpo);

        ErrorText = !valid ? StrInvalidDuration
            : mtpd is null && rto is null && rpo is null ? StrNothingDeclared
            : rto is { } r && mtpd is { } m && r > m ? StrRtoExceedsMtpd
            : string.Empty;

        this.RaisePropertyChanged(nameof(HasError));
        IsSaveEnabled = string.IsNullOrEmpty(ErrorText);
    }

    private async Task SaveAsync()
    {
        if (!IsSaveEnabled) return;

        ContinuitySummary.TryToMinutes(MtpdValue, MtpdUnit.Unit, out var mtpd);
        ContinuitySummary.TryToMinutes(RtoValue, RtoUnit.Unit, out var rto);
        ContinuitySummary.TryToMinutes(RpoValue, RpoUnit.Unit, out var rpo);

        var request = new BusinessImpactAnalysisRequest
        {
            MtpdMinutes = mtpd,
            RtoMinutes = rto,
            RpoMinutes = rpo,
            AssessedAt = AssessedAt?.UtcDateTime,
            Notes = Notes
        };

        var saved = false;
        await RunAsync(Localizer["BiaSavedMSG"], async () =>
        {
            await ContinuityService.SaveBiaAsync(_entityId, request);
            saved = true;
        });

        if (saved) Close(new ContinuityDialogResult { Action = ResultActions.Ok, Changed = true });
    }
}
