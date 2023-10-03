using System.Collections.ObjectModel;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.Extensions.Localization;
using ReactiveUI;
using System.Reactive;
using Avalonia.Media;

namespace GUIClient.ViewModels;

public class VulnerabilitiesViewModel: ViewModelBase
{
    #region LANGUAGE

    private string StrVulnerabilities { get;  } = Localizer["Vulnerabilities"];
    private string StrReload { get;  } = Localizer["Reload"];
    private string StrImport { get;  } = Localizer["Import"];
    private string StrFirstDetection { get;  } = Localizer["FirstDetection"];
    private string StrLastDetection { get;  } = Localizer["LastDetection"];
    private string StrStatus { get;  } = Localizer["Status"];
    private string StrDetectionCount { get;  } = Localizer["DetectionCount"];
    private string StrTitle { get;  }= Localizer["Title"];
    private string StrTechnology { get;  }= Localizer["Technology"];
    private string StrDetails { get; } = Localizer["Details"];

    #endregion
    
    #region PROPERTIES

    private string _statsRows = "Rows: 0";
    private string StatsRows {
        get => _statsRows;
        set => this.RaiseAndSetIfChanged(ref _statsRows, value);
    }

    private int _rowCount = 0;
    private int RowCount {
        get => _rowCount;
        set {
            _rowCount = value;
            StatsRows = $"Rows: {value}";
        }
    }

    private ObservableCollection<Vulnerability> _vulnerabilities = new ObservableCollection<Vulnerability>();
    public ObservableCollection<Vulnerability> Vulnerabilities {
        get => _vulnerabilities;
        set => this.RaiseAndSetIfChanged(ref _vulnerabilities, value);
    }

    private bool _isDetailsPanelOpen = false;

    public bool IsDetailsPanelOpen
    {
        get => _isDetailsPanelOpen;
        set => this.RaiseAndSetIfChanged(ref _isDetailsPanelOpen, value);
    }

    private RotateTransform _detailRotation = new(90);

    public RotateTransform DetailRotation
    {
        get => _detailRotation;
        set => this.RaiseAndSetIfChanged(ref _detailRotation, value);
    }
    
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    
    #endregion

    #region BUTTONS

    public ReactiveCommand<Unit, Unit> BtReloadClicked { get; } 
    public ReactiveCommand<Unit, Unit> BtDetailsClicked { get; } 

    #endregion

    #region FIELDS

    private bool _initialized = false;

    #endregion
    
    
    public VulnerabilitiesViewModel()
    {
        DetailRotation = new RotateTransform(90);
        
        BtReloadClicked = ReactiveCommand.Create(ExecuteReload);
        BtDetailsClicked = ReactiveCommand.Create(ExecuteOpenCloseDetails);
        
        AuthenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
        };
    }

    #region METHODS
    
    private void Initialize()
    {
        if (!_initialized)
        {
            Vulnerabilities = new ObservableCollection<Vulnerability>(VulnerabilitiesService.GetAll());
            RowCount = Vulnerabilities.Count;
                
            _initialized = true;
        }
    }

    private void ExecuteReload()
    {
        Vulnerabilities = new ObservableCollection<Vulnerability>(VulnerabilitiesService.GetAll());
        RowCount = Vulnerabilities.Count;
    }

    private void ExecuteOpenCloseDetails()
    {
        //IsDetailsPanelOpen = !IsDetailsPanelOpen;

        if (IsDetailsPanelOpen)
        {
            IsDetailsPanelOpen = false;
            DetailRotation = new RotateTransform(90);
        }
        else
        {
            IsDetailsPanelOpen = true;
            DetailRotation = new RotateTransform(0);
        }
    }

    #endregion
}