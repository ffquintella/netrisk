using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.Views;
using Model;
using Model.Authentication;
using Model.Status;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Serilog;
using Tools.Helpers;
using TimeSpan = System.TimeSpan;

namespace GUIClient.ViewModels;

public class EditIncidentViewModel: ViewModelBase
{

    #region LANGUAGE

    private string StrIdentification => Localizer["Identification"];
    private string StrYear => Localizer["Year"] + ":";
    private string StrSequence => Localizer["Sequence"]+ ":";
    private string StrName => Localizer["Name"]+ ":";
    private string StrEnableFreeNaming => Localizer["Enable free naming"];
    private string StrIncidentDetails => Localizer["Incident details"];
    private string StrCreationDate => Localizer["Creation date"] + ":";
    private string StrIncidentDates => Localizer["Incident dates"];
    private string StrReportDate => Localizer["Report date"] + ":";
    private string StrDuration => Localizer["Duration"] + " (" + Localizer["Hours"] + ")" + ":";
    private string StrStatus => Localizer["Status"] + ":";
    
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
    
    private bool _enableFreeNaming;
    
    public bool EnableFreeNaming
    {
        get => _enableFreeNaming;
        set
        {
            if (IsEdit)
            {
                IsEditAndNotFreeNaming = !value;
                IsEditOrFreeNaming = true;
            }
            else if (IsCreate)
            {
                IsCreateAndNotFreeNaming = !value;
                IsEditOrFreeNaming = value;
            }
            this.RaiseAndSetIfChanged(ref _enableFreeNaming, value);
        }
    }

    private bool _isEditAndNotFreeNaming;
    
    public bool IsEditAndNotFreeNaming
    {
        get => _isEditAndNotFreeNaming;
        set => this.RaiseAndSetIfChanged(ref _isEditAndNotFreeNaming, value);
    }
    
    private bool _isCreateAndNotFreeNaming;
    
    public bool IsCreateAndNotFreeNaming
    {
        get => _isCreateAndNotFreeNaming;
        set => this.RaiseAndSetIfChanged(ref _isCreateAndNotFreeNaming, value);
    }
    
    private bool _isEditOrFreeNaming;
    
    public bool IsEditOrFreeNaming
    {
        get => _isEditOrFreeNaming;
        set => this.RaiseAndSetIfChanged(ref _isEditOrFreeNaming, value);
    }
    
    public DateTimeOffset SelectedYear
    {
        get => new DateTimeOffset (new DateTime( Incident.Year, 1, 1));
        set
        {
            Incident.Year = value.Year;
            _ = AdjustIncidentName();
        }
    }
    
    public DateTimeOffset ReportDate
    {
        get => new DateTimeOffset(Incident.ReportDate);
        set => Incident.ReportDate = value.DateTime;
    }

    public decimal Duration
    {
        get
        {
            if(Incident.Duration == null)
            {
                Incident.Duration = new TimeSpan(1,0,0);
            }
            return Convert.ToDecimal(Incident.Duration.Value.TotalHours);
        }
        set => Incident.Duration = TimeSpan.FromHours(Convert.ToDouble(value));
    }

    public decimal SelectedSequence
    {
        get => Incident.Sequence;
        set
        {
            Incident.Sequence = Convert.ToInt32(value);
            _ = AdjustIncidentName();
        }
    }

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            Incident.Name = value;
            this.RaiseAndSetIfChanged(ref _name, value);
        }
    }
    
    public List<IntStatusItem> StatusItems { get; } = IncidentStatus.GetAll(Localizer);
    
    public IntStatusItem SelectedStatus
    {
        get => StatusItems.Find(x => x.IntStatus == Incident.Status) ?? StatusItems.FirstOrDefault(x => x.IntStatus == (int)IntStatus.Active)!;
        set => Incident.Status = value.IntStatus;
    }

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
            OperationType.Create =>  Incident = new Incident(),
            _ => throw new Exception("Invalid operation type")
        };
       
       
        _ = LoadDataAsync();
        
    }
    
    #endregion
    
    #region METHODS

    private async Task AdjustIncidentName()
    {
        string fmt = "0000.##";
        
        if(IsCreate)
        {
            Name = $"SI-{Incident.Year}-{Incident.Sequence.ToString(fmt)}";
        }
    }
    
    private async Task LoadDataAsync()
    {
        // Get authenticated user info
        UserInfo = AuthenticationService.AuthenticatedUserInfo;

        EnableFreeNaming = false;
        
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
        
        // Get Incident Name
        if (IsCreate)
        {
            Incident.Year = DateTime.Now.Year;
            Incident.Sequence = 1;
            Incident.CreationDate = DateTime.Now;
            Incident.ReportDate = DateTime.Now;
            Incident.Duration = new TimeSpan(1, 0, 0);
            //Incident.Name = Localizer["Not defined"];
            await AdjustIncidentName();
        }
        
        
    }
    
    #endregion
    
}