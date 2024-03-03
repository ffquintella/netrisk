using System;
using System.Collections.ObjectModel;
using System.Linq;
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
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Tools.String;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;

namespace GUIClient.ViewModels.Reports;

public class FileReportsViewModel : ReportsViewModelBase
{
    #region LANGUAGE

    public string StrFiles { get; } = Localizer["Files"];
    public string StrReports { get; } = Localizer["Reports"];
    public string StrName { get; } = Localizer["Name"];
    public string StrSubmissionDate { get; } = Localizer["SubmissionDate"];
    public string StrOperations { get; } = Localizer["Operations"];
    public string StrStatus { get; } = Localizer["Status"];
    private string StrSaveDocumentMsg { get; } = Localizer["SaveDocumentMsg"];
 

    #endregion

    #region PROPERTIES

    private ObservableCollection<DAL.Entities.Report> _reports = new ObservableCollection<Report>();

    public ObservableCollection<Report> Reports
    {
        get => _reports;
        set => this.RaiseAndSetIfChanged(ref _reports, value);
    }
    
    private Window? ParentWindow { get; set; }

    #endregion

    #region SERVICES

    private IDialogService DialogService { get; } = GetService<IDialogService>();
    private IReportsService ReportsService { get; } = GetService<IReportsService>();
    private IFilesService FilesService { get; } = GetService<IFilesService>();
    
    #endregion
    
    #region BUTTONS
    public ReactiveCommand<int, Unit> BtFileDownloadClicked { get; }
    public ReactiveCommand<int, Unit> BtFileDeleteClicked { get; }
    #endregion
    
    #region METHODS


    public FileReportsViewModel(Window parentWindow) 
    {
        ParentWindow = parentWindow;
        BtFileDownloadClicked = ReactiveCommand.Create<int>(ExecuteFileDownload);
        BtFileDeleteClicked = ReactiveCommand.Create<int>(ExecuteFileDelete);
        Initialize();
    }
    
    public async void ExecuteFileDownload(int id)
    {

        var fileDespritor = await FilesService.GetByIdAsync(id);
        
        if (fileDespritor == null) throw new Exception("File not found");
        
        var topLevel = TopLevel.GetTopLevel(ParentWindow);
        
        var file = await topLevel!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = StrSaveDocumentMsg,
            DefaultExtension = FilesService.ConvertTypeToExtension(fileDespritor.Type!),
            SuggestedFileName = StringCleaner.ReplaceNonAlphanumeric(fileDespritor.Name) + FilesService.ConvertTypeToExtension(fileDespritor.Type!),
            
        });

        if (file == null) return;
            
        FilesService.DownloadFile(fileDespritor.UniqueName, file.Path);
        
    }

    public async void ExecuteFileDelete(int id)
    {
        var messageBoxConfirm = MessageBoxManager
            .GetMessageBoxStandard(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["ReportDeleteConfirmationMSG"]  ,
                ButtonDefinitions = ButtonEnum.OkAbort,
                Icon = Icon.Question,
            });
                        
        var confirmation = await messageBoxConfirm.ShowAsync();

        if (confirmation == ButtonResult.Ok)
        {
            try
            {
                await ReportsService.DeleteReportAsync(id);
                
                var reportItem = Reports.FirstOrDefault(r => r.Id == id);
                if(reportItem != null)
                    Reports.Remove(reportItem);
                
            }catch(Exception e)
            {
                Log.Error("Error deleting report {Message}", e.Message);
                var messageBoxStandardWindow2 = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["ErrorDeletingReportMSG"]  ,
                        Icon = Icon.Warning,
                    });
                        
                await messageBoxStandardWindow2.ShowAsync(); 
            }

        }
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