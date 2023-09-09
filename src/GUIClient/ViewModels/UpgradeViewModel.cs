using System;
using System.Net;
using System.Threading;
using ClientServices.Interfaces;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class UpgradeViewModel: ViewModelBase
{
    private int _progressBarValue = 0;
    public int ProgressBarValue
    {
        get => _progressBarValue;
        set
        {
            this.RaiseAndSetIfChanged(ref _progressBarValue, value);
        }
    }
    public int ProgressBarMaxValue { get; set; } = 20;
    
    private string _operation = " --- ";
    public string Operation
    {
        get => _operation;
        set
        {
            this.RaiseAndSetIfChanged(ref _operation, value);
        }
    }

    private ISystemService  _systemService;
    public UpgradeViewModel()
    {
        _systemService = GetService<ISystemService>();
    }

    public void StartUpgrade()
    {
        Operation = "Starting upgrade"; 
        ProgressBarValue += 5;
        
        //Thread.Sleep(1000);
        //var tmpDir = _systemService.GetTempPath();

        Operation = "Getting upgrade script";
        _systemService.DownloadUpgradeScript();
        ProgressBarValue += 5;
        
        _systemService.DownloadApplication();
        ProgressBarValue += 10;

    }
}