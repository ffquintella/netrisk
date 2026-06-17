using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Tools;
using GUIClient.Views;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class RisksPanelViewModel: ViewModelBase
{
    private bool _initialized = false;
    public string StrSubject { get;  } 
    public string StrStatus { get;  }
    public string StrSubmissionDate { get;  }
    
    private ObservableCollection<Risk> _risks;

    public ObservableCollection<Risk> Risks
    {
        get => _risks;
        set => this.RaiseAndSetIfChanged(ref _risks, value);
    }
    
    private readonly IRisksService _risksService;
    private readonly IExportClientService _exportService;
    
    public string FilterText { get; set; } = "";
    
    public ReactiveCommand<Unit, Unit> ApplyFilterCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCommand { get; }

    public RisksPanelViewModel()
    {
        _risksService = GetService<IRisksService>();
        _exportService = GetService<IExportClientService>();
        _risks =  new ObservableCollection<Risk>(new List<Risk>());
        
        ApplyFilterCommand = ReactiveCommand.CreateFromTask(LoadRisks);
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync);
        
        StrSubject= Localizer["Subject"];
        StrStatus = Localizer["Status"];
        StrSubmissionDate = Localizer["SubmissionDate"];
    }

    public async Task InitializeAsync()
    {
        if (!_initialized)
        {
            await LoadRisks();
            _initialized = true;
        }
    }
    
    private async Task LoadRisks()
    {
        Risks = new ObservableCollection<Risk>(await _risksService.GetFilteredAsync(FilterText));
    }
    
    private async Task ExportAsync()
    {
        var owner = WindowsManager.AllWindows.Find(w => w is MainWindow);

        var format = await ExportFileSaver.PickFormatAsync(
            owner,
            Localizer["Export"],
            Localizer["Choose the export format"]);

        if (format is null) return;

        var data = await _exportService.ExportAsync("Risk", format.Value, FilterText);

        await ExportFileSaver.SaveAsync(owner, format.Value, data);
    }
}