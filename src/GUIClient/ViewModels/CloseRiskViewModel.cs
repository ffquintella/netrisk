using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Interfaces;
using GUIClient.Validation;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Exceptions;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using Serilog;

namespace GUIClient.ViewModels;

/// <summary>
/// Records the closure of a risk. Migrated onto the single dialog stack (IX-2), so Esc/Ctrl+S,
/// owner-centring and the typed result all come from <see cref="DialogWindowBase{TResult}"/>
/// rather than being hand-rolled here.
/// </summary>
public class CloseRiskViewModel
    : ParameterizedDialogViewModelBase<CloseRiskDialogResult, CloseRiskDialogParameter>, ISaveableDialog
{
    #region LANGUAGE

        public string StrCloseRisk { get; } = Localizer["CloseRisk"];
        public string StrReason { get; } = Localizer["Reason"];
        public string StrNotes { get; } = Localizer["Notes"];
        public new string StrSave { get; } = Localizer["Save"];
        public new string StrCancel { get; } = Localizer["Cancel"];

    #endregion

    #region PROPERTIES

        private List<CloseReason> _closeReasons = new ();
        public List<CloseReason> CloseReasons
        {
            get => _closeReasons;
            set => this.RaiseAndSetIfChanged(ref _closeReasons, value);
        }

        private CloseReason? _selectedCloseReason;
        public CloseReason? SelectedCloseReason
        {
            get => _selectedCloseReason;
            set => this.RaiseAndSetIfChanged(ref _selectedCloseReason, value);
        }

        private bool _saveEnabled;
        public bool SaveEnabled
        {
            get => _saveEnabled;
            set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
        }

        private string _notes = "";
        public string Notes
        {
            get => _notes;
            set => this.RaiseAndSetIfChanged(ref _notes, value);
        }

    #endregion

    #region COMMANDS

        public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
        public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

        /// <inheritdoc />
        public ICommand? SaveCommand => BtSaveClicked;

    #endregion

    #region INTERNAL FIELDS

        private Risk? _risk;
        private readonly IRisksService _risksService;
        private readonly IAuthenticationService _authenticationService;

    #endregion

    #region CONSTRUCTOR

    public CloseRiskViewModel()
    {
        _risksService = GetService<IRisksService>();
        _authenticationService = GetService<IAuthenticationService>();

        CloseReasons = _risksService.GetRiskCloseReasons();

        BtSaveClicked = ReactiveCommand.CreateFromTask(ExecuteSaveAsync,
            this.WhenAnyValue(x => x.SaveEnabled));
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);

        this.ValidationRule(
            viewModel => viewModel.SelectedCloseReason,
            val => val != null,
            Localizer["PleaseSelectOneMSG"]);

        this.IsValid()
            .Subscribe(isValid => { SaveEnabled = isValid; });
    }

    #endregion

    #region METHODS

    public override void Activate(CloseRiskDialogParameter parameter)
    {
        _risk = parameter.Risk;
    }

    private async Task ExecuteSaveAsync()
    {
        if (_risk == null || SelectedCloseReason == null) return;

        try
        {
            var closure = new Closure
            {
                RiskId = _risk.Id,
                UserId = _authenticationService.AuthenticatedUserInfo!.UserId!.Value,
                ClosureDate = DateTime.Now,
                CloseReason = SelectedCloseReason.Value,
                Note = Notes
            };

            _risksService.CloseRisk(closure);

            Close(new CloseRiskDialogResult { Action = ResultActions.Ok });
        }
        catch (RestComunicationException ex)
        {
            Log.Warning("Rest error closing risk: {Message}", ex.Message);

            // IX-4: the dialog stays open with the input intact so the user can retry.
            await MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["RiskClosingErrorMSG"] + "\n" + ex.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                })
                .ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Unknown error closing risk: {Message}", ex.Message);

            await MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["RiskClosingErrorMSG"] + "\n" + ex.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                })
                .ShowAsync();
        }
    }

    private void ExecuteCancel() => Close(new CloseRiskDialogResult { Action = ResultActions.Cancel });

    #endregion
}
