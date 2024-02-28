using System.Collections.ObjectModel;
using DAL.Entities;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;

namespace GUIClient.ViewModels.Reports;

public class FileReportsViewModel : ReportsViewModelBase
{
    #region LANGUAGE

    public string StrFiles { get; } = Localizer["Files"];
    public string StrReports { get; } = Localizer["Reports"];
    public string StrName { get; } = Localizer["Name"];
    public string StrSubmissionDate { get; } = Localizer["SubmissionDate"];
    public string StrOperations { get; } = Localizer["Operations"];

    #endregion

    #region PROPERTIES

    private ObservableCollection<DAL.Entities.Report> _reports = new ObservableCollection<Report>();

    public ObservableCollection<Report> Reports
    {
        get => _reports;
        set => this.RaiseAndSetIfChanged(ref _reports, value);
    }

    #endregion

    #region SERVICES

    private IDialogService DialogService { get; } = GetService<IDialogService>();
    #endregion
    
    #region METHODS


    public FileReportsViewModel()
    {
        
    }
    
    public async void ExecuteAddReport()
    {
        var parameter = new ReportDialogParameter();
        
        var dialogCreate = await DialogService.ShowDialogAsync<ReportDialogResult, ReportDialogParameter>(nameof(CreateReportDialogViewModel), parameter);
    }
    
    #endregion

}