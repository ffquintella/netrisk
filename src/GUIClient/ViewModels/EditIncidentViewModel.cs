using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.Views;
using Model.Authentication;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Serilog;

namespace GUIClient.ViewModels;

public class EditIncidentViewModel: ViewModelBase
{

    #region LANGUAGE

    private string StrIdentification => Localizer["Identification"];
    
    #endregion

    #region FIELDS

    #endregion
    
    #region PROPERTIES
    
    private OperationType WindowOperationType { get; set; } 
    
    private Incident _incident = new ();
    
    public Incident Incident
    {
        get => _incident;
        set => this.RaiseAndSetIfChanged(ref _incident, value);
    }
    
    public string WindowTitle => WindowOperationType switch
    {
        OperationType.Edit => Localizer["Edit Incident"],
        OperationType.Create => Localizer["Create Incident"],
        _ => throw new Exception("Invalid operation type")
    };
    
    private string _footerText = string.Empty;
    
    public string FooterText
    {
        get => _footerText;
        set => this.RaiseAndSetIfChanged(ref _footerText, value);
    }
    
    private AuthenticatedUserInfo? _userInfo;
    public AuthenticatedUserInfo? UserInfo
    {
        get => _userInfo;
        set => this.RaiseAndSetIfChanged(ref _userInfo, value);
    }
    
    public EditIncidentWindow? ParentWindow { get; set; }
    
    public bool IsCreate => WindowOperationType == OperationType.Create;
    public bool IsEdit => WindowOperationType == OperationType.Edit;
    
    #endregion
    
    #region SERVICES
    private IAuthenticationService AuthenticationService { get; } = GetService<IAuthenticationService>();
    #endregion
    
    #region COMMANDS
    
    #endregion
    
    #region CONSTRUCTOR

    public EditIncidentViewModel() : this(OperationType.Create)
    {
        
    }
    
    public EditIncidentViewModel(Incident incident) : this(OperationType.Edit, incident)
    {
        
    }

    public EditIncidentViewModel(OperationType operationType, Incident? incident = null)
    {
        WindowOperationType = operationType;

        Incident = WindowOperationType switch
        {
            OperationType.Edit => Incident = incident ?? throw new ArgumentNullException(nameof(incident)),
            OperationType.Create => Incident = new Incident(),
            _ => throw new Exception("Invalid operation type")
        };
        
        _ = LoadDataAsync();
        
    }
    
    #endregion
    
    #region METHODS
    
    private async Task LoadDataAsync()
    {
        UserInfo = AuthenticationService.AuthenticatedUserInfo;
        
        if(UserInfo == null)
        {
            var msgBox1 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["Not authenticated"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox1.ShowAsync();
            
            Log.Error("Cloud not retrieve authenticated user info");
            
            ParentWindow?.Close();
            
        }
        
        FooterText = $"{Localizer["Logged as"]}: {UserInfo?.UserName}";
        
    }
    
    #endregion
    
}