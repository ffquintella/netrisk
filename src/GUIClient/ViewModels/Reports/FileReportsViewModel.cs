using System;
using System.Collections.ObjectModel;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Reports;
using ReactiveUI;
using System.Text.Json;
using DAL.EntitiesDto;
using Model;
using Serilog;

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
    private IReportsService ReportsService { get; } = GetService<IReportsService>();
    
    #endregion
    
    #region METHODS


    public FileReportsViewModel()
    {
            Initialize();
    }
    
    public async void Initialize()
    {
        try
        {
            var reports = await ReportsService.GetReportsAsync();
            if(reports != null)
                Reports = new ObservableCollection<Report>(reports);
        }catch(Exception e)
        {
            Log.Error("Error getting reports {Message}", e.Message);
        }
    }
    
    public async void ExecuteAddReport()
    {
        var parameter = new ReportDialogParameter();

        var loggedInUser = AuthenticationService.AuthenticatedUserInfo!;
        
        var dialogCreate = await DialogService.ShowDialogAsync<ReportDialogResult, ReportDialogParameter>(nameof(CreateReportDialogViewModel), parameter);
        
        if (dialogCreate != null && dialogCreate.Action == ResultActions.Ok)
        {
            var reportType = dialogCreate.ReportType;

            var name = "";

            switch (reportType)
            {
                case 0:
                    name = Localizer["DetailedEntitiesRisks"];
                    break;
            }
            
            var parameters = new ReportParameters
            {
                ReportType = reportType
            };
            
            var reportDto = new ReportDto
            {
                Name = name,
                CreationDate = DateTime.Now,
                CreatorId = loggedInUser.UserId!.Value,
                Type = reportType,
                Parameters =  JsonSerializer.Serialize(parameters),
                Status = (int)IntStatus.New
                
            };

            try
            {
                var report = await ReportsService.CreateReportAsync(reportDto);
            
                if(report != null)
                    Reports.Add(report);
                
            }catch(Exception e)
            {
                Log.Error("Error creating report {Message}", e.Message);
            }


            //Reports.Add(report);
        }
    }
    
    #endregion

}