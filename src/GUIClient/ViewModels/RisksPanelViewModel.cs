using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
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
    public ReactiveCommand<Unit, Unit> ExportPdfCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCsvCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportXlsxCommand { get; }

    public RisksPanelViewModel()
    {
        _risksService = GetService<IRisksService>();
        _exportService = GetService<IExportClientService>();
        _risks =  new ObservableCollection<Risk>(new List<Risk>());
        
        ApplyFilterCommand = ReactiveCommand.CreateFromTask(LoadRisks);
        ExportPdfCommand = ReactiveCommand.CreateFromTask(() => ExportAsync(ExportFormat.Pdf));
        ExportCsvCommand = ReactiveCommand.CreateFromTask(() => ExportAsync(ExportFormat.Csv));
        ExportXlsxCommand = ReactiveCommand.CreateFromTask(() => ExportAsync(ExportFormat.Xlsx));
        
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
    
    private async Task ExportAsync(ExportFormat format)
    {
        var data = await _exportService.ExportAsync("Risk", format, FilterText);

        var dialog = new SaveFileDialog();
        dialog.Filters.Add(new FileDialogFilter() { Name = format.ToString(), Extensions = { format.ToString().ToLower() } });
        var result = await dialog.ShowAsync(WindowsManager.AllWindows.Find(w => w is MainWindow)!);
        if(result != null)
        {
            await File.WriteAllBytesAsync(result, data);
        }
    }
}