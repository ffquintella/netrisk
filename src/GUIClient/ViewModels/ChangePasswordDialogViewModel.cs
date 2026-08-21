using RxVoid = ReactiveUI.Primitives.RxVoid;
using GUIClient.Interfaces;
using System.Windows.Input;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using System;
using Tools.Security;

namespace GUIClient.ViewModels;

public class ChangePasswordDialogViewModel: DialogViewModelBase<StringDialogResult>, ISaveableDialog
{
    
    #region LANGUAGE

    public string StrTitle => Localizer["ChangePassword"];
    public string StrPassword => Localizer["Password"];
    public string StrConfirmation => Localizer["Confirmation"];
    public new string StrSave => Localizer["Save"];
    public new string StrCancel => Localizer["Cancel"];
    
    #endregion
    
    
    #region PROPERTIES
    
    private bool _saveEnabled = false;
    public bool SaveEnabled
    {
        get => _saveEnabled;
        set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
    }
    
    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }
    private string _confirmation = string.Empty;
    public string Confirmation
    {
        get => _confirmation;
        set => this.RaiseAndSetIfChanged(ref _confirmation, value);
    }
    
    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

    /// <inheritdoc />
    public ICommand? SaveCommand => BtSaveClicked;

    #endregion

    #region SERVICES
    #endregion
    
    public ChangePasswordDialogViewModel()
    {
        BtSaveClicked = ReactiveCommand.Create(ExecuteSave,
            this.WhenAnyValue(x => x.SaveEnabled));
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);

        
        //Tools.Security.PasswordTools.CheckPasswordComplexity(password);
        
        this.ValidationRule(
            viewModel => viewModel.Password, 
            pwd => PasswordTools.CheckPasswordComplexity(pwd),
            Localizer["PasswordInvalid"]);

        var confirmationMatches = this.WhenAnyValue(
            x => x.Password,
            x => x.Confirmation,
            (password, confirmation) =>
                PasswordTools.CheckPasswordComplexity(confirmation) && confirmation == password);

        this.ValidationRule(
            viewModel => viewModel.Confirmation,
            confirmationMatches,
            Localizer["ConfirmationInvalid"]);
        
        
        this.IsValid()
            .Subscribe(x =>
            {
                SaveEnabled = x;
            });
    }
    
    #region METHODS

    private void ExecuteCancel()
    {
        var result = new StringDialogResult
        {
            Result = "",
            Action = ResultActions.Cancel
        };
        Close(result);
    }
    
    private void ExecuteSave()
    {
        var result = new StringDialogResult
        {
            Result = Password,
            Action = ResultActions.Ok
        };
        Close(result);
    }
    
    #endregion
}