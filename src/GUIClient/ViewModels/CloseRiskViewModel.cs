﻿using System.Collections.Generic;
using System;
using System.Reactive;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;
using Serilog;


namespace GUIClient.ViewModels;

public class CloseRiskViewModel: ViewModelBase
{
    #region LANGUAGE

        public string StrCloseRisk { get; }
        public string StrReason { get; }
        public string StrNotes { get; }
        public string StrSave { get; }
        public string StrCancel { get; }
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
            get
            {
                return _saveEnabled;
            }
            set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
        }

        private string _notes = "";
        public string Notes
        {
            get => _notes;
            set => this.RaiseAndSetIfChanged(ref _notes, value);
        }
        
        public ReactiveCommand<Window, Unit> BtSaveClicked { get; }
        public ReactiveCommand<Window, Unit> BtCancelClicked { get; }
    #endregion

    #region INTERNAL FIELDS
        private Risk _risk;
        private readonly IRisksService _risksService;
        private readonly IAuthenticationService _authenticationService;

    #endregion

    public CloseRiskViewModel(Risk risk)
    {
        StrCloseRisk = Localizer["CloseRisk"];
        StrReason = Localizer["Reason"];
        StrNotes = Localizer["Notes"];
        StrSave = Localizer["Save"];
        StrCancel = Localizer["Cancel"];
        
        _risk = risk;
        _risksService = GetService<IRisksService>();
        _authenticationService = GetService<IAuthenticationService>();
        
        CloseReasons = _risksService.GetRiskCloseReasons();
        
        BtSaveClicked = ReactiveCommand.Create<Window>(ExecuteSave);
        BtCancelClicked = ReactiveCommand.Create<Window>(ExecuteCancel);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedCloseReason, 
            val => val != null,
            Localizer["PleaseSelectOneMSG"]);

        this.IsValid()
            .Subscribe(isValid => { SaveEnabled = isValid; });
    }
    
    #region METHODS

    private async void ExecuteSave(Window baseWindow)
    {

        try
        {
            var closure = new Closure
            {
                RiskId = _risk.Id,
                UserId = _authenticationService.AuthenticatedUserInfo!.UserId!.Value,
                ClosureDate = DateTime.Now,
                CloseReason = SelectedCloseReason!.Value,
                Note = Notes
            };
            _risksService.CloseRisk(closure);
            baseWindow.Close();
        }
        catch (RestComunicationException ex)
        {
            var msgOk = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["RiskClosingErrorMSG"] + "\n" + ex.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            Log.Warning("Rest error closing risk: {Message}", ex.Message);
            
            await msgOk.ShowAsync();

        }
        catch (Exception ex)
        {
            Log.Error("Unknown error closing risk: {Message}", ex.Message);
        }
    }
    
    private void ExecuteCancel(Window baseWindow)
    {
        baseWindow.Close();
    }

    #endregion
    
    
}