using System.Collections.ObjectModel;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.Extensions.Localization;
using ReactiveUI;
using System.Reactive;

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

    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    
    #endregion

    #region BUTTONS

    public ReactiveCommand<Unit, Unit> BtReloadClicked { get; } 

    #endregion

    #region FIELDS

    private bool _initialized = false;

    #endregion
    
    
    public VulnerabilitiesViewModel()
    {

        BtReloadClicked = ReactiveCommand.Create(ExecuteReload);
        
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

    #endregion
}