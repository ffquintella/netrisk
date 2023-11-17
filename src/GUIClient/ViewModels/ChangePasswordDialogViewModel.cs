using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;
using System;
using Tools.Security;

namespace GUIClient.ViewModels;

public class ChangePasswordDialogViewModel: DialogViewModelBase<StringDialogResult>
{
    
    #region LANGUAGE

    public string StrTitle => Localizer["ChangePassword"];
    public string StrPassword => Localizer["Password"];
    public string StrConfirmation => Localizer["Confirmation"];
    public string StrSave => Localizer["Save"];
    public string StrCancel => Localizer["Cancel"];
    
    #endregion
    
    
    #region PROPERTIES
    
    private bool _saveEnabled = false;
    public bool SaveEnabled
    {
        get => _saveEnabled;
        set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
    }
    
    private string _password;
    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }
    private string _confirmation;
    public string Confirmation
    {
        get => _confirmation;
        set => this.RaiseAndSetIfChanged(ref _confirmation, value);
    }
    
    #endregion
    
    #region SERVICES
    #endregion
    
    public ChangePasswordDialogViewModel()
    {
        
        //Tools.Security.PasswordTools.CheckPasswordComplexity(password);
        
        this.ValidationRule(
            viewModel => viewModel.Password, 
            pwd => PasswordTools.CheckPasswordComplexity(pwd),
            Localizer["PasswordInvalid"]);

        this.ValidationRule(
            viewModel => viewModel.Confirmation, 
            pwd => PasswordTools.CheckPasswordComplexity(pwd) && pwd == Password,
            Localizer["ConfirmationInvalid"]);
        
        
        this.IsValid()
            .Subscribe(x =>
            {
                SaveEnabled = x;
            });
    }
    
    #region METHODS

    public void BtCancelClicked()
    {
        var result = new StringDialogResult
        {
            Result = "",
            Action = ResultActions.Cancel
        };
        Close(result);
    }
    
    public void BtSaveClicked()
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