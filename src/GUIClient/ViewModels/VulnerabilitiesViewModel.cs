using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.Extensions.Localization;
using ReactiveUI;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClientServices.Services;
using DynamicData;
using DynamicData.Binding;
using GUIClient.Models;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using GUIClient.Views;
using Model;
using Model.Authentication;
using Model.DTO;
using Model.Exceptions;
using Model.Globalization;
using Model.Vulnerability;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using Serilog;


namespace GUIClient.ViewModels;

public class VulnerabilitiesViewModel: ViewModelBase
{
    #region LANGUAGE

    public string StrVulnerabilities { get;  } = Localizer["Vulnerabilities"];
    public string StrReload { get;  } = Localizer["Reload"];
    public string StrImport { get;  } = Localizer["Import"];
    public string StrFirstDetection { get;  } = Localizer["FirstDetection"];
    public string StrLastDetection { get;  } = Localizer["LastDetection"];
    public string StrStatus { get;  } = Localizer["Status"];
    public string StrDetectionCount { get;  } = Localizer["DetectionCount"];
    public string StrTitle { get;  }= Localizer["Title"];
    public string StrTechnology { get;  }= Localizer["Technology"];
    public string StrDetails { get; } = Localizer["Details"];
    public string StrAnalyst { get; } = Localizer["Analyst"];
    public string StrFixTeam { get; } = Localizer["FixTeam"];
    public string StrHost { get; } = Localizer["Host"];
    public string StrName { get; } = Localizer["Name"];
    public string StrEdit { get; } = Localizer["Edit"];
    public string StrScore { get; } = Localizer["Score"];
    public string StrImpact { get; } = Localizer["Impact"];
    public string StrTeamResponsible { get; } = Localizer["TeamResponsible"];
    public string StrRisks { get; } = Localizer["Risks"];
    public string StrSubject { get; } = Localizer["Subject"];
    public string StrCategory { get; } = Localizer["Category"];
    public string StrSource { get; } = Localizer["Source"];
    public string StrAdd {get; } = Localizer["Add"];
    public string StrVerify {get; } = Localizer["Verify"];
    public string StrDelete {get; } = Localizer["Delete"];
    public string StrReject {get; } = Localizer["Reject"];
    public string StrDescription {get; } = Localizer["Description"];
    public string StrComments {get; } = Localizer["Comments"];
    public string StrRequestFix {get; } = Localizer["RequestFix"];
    public string StrActions {get; } = Localizer["Actions"];
    public string StrClose {get; } = Localizer["Close"];
    public string StrPrioritize {get; } = Localizer["Prioritize"];
    public string StrFilter {get; } = Localizer["Filter"];
    public string StrApply {get; } = Localizer["Apply"];
    public string StrHideFilter {get; } = Localizer["HideFilter"];
    public string StrSolution {get; } = Localizer["Solution"];
    public string StrExploitAvailable {get; } = Localizer["ExploitAvailable"] ;
    public string StrThreatIntensity {get; } = Localizer["ThreatIntensity"] ;
    public string StrThreatRecency {get; } = Localizer["ThreatRecency"] ;
    public string StrThreatSources {get; } = Localizer["ThreatSources"] ;
    public string StrVulnerabilityPublicationDate {get; } = Localizer["VulnerabilityPublicationDate"] ;
    public string StrPatchPublicationDate {get; } = Localizer["PatchPublicationDate"] ;
    public string StrApplication {get; } = Localizer["Application_2"] ;
    
    

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

    private ObservableCollection<Vulnerability> _vulnerabilities = new ();
    public ObservableCollection<Vulnerability> Vulnerabilities {
        get => _vulnerabilities;
        set => this.RaiseAndSetIfChanged(ref _vulnerabilities, value);
    }
    
    private ObservableCollection<LocalizableListItem> _impacts = new ();
    public ObservableCollection<LocalizableListItem> Impacts {
        get => _impacts;
        set => this.RaiseAndSetIfChanged(ref _impacts, value);
    }

    private bool _isDetailsPanelOpen;

    public bool IsDetailsPanelOpen
    {
        get => _isDetailsPanelOpen;
        set => this.RaiseAndSetIfChanged(ref _isDetailsPanelOpen, value);
    }

    private bool _filterIsVisible;
    public bool FilterIsVisible
    {
        get => _filterIsVisible;
        set => this.RaiseAndSetIfChanged(ref _filterIsVisible, value);
    }
    
    

    private RotateTransform _detailRotation = new(90);

    public RotateTransform DetailRotation
    {
        get => _detailRotation;
        set => this.RaiseAndSetIfChanged(ref _detailRotation, value);
    }
    
    private ObservableCollection<RiskScoring>? _selectedVulnerabilityRisksScores;

    private ObservableCollection<RiskScoring>? SelectedVulnerabilityRisksScores
    {
        get => _selectedVulnerabilityRisksScores;
        set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityRisksScores, value);
    }

    private Vulnerability? _selectedVulnerability;
    
    public Vulnerability? SelectedVulnerability
    {
        get => _selectedVulnerability;
        set
        {
            if (value != null)
            {
                LoadVulnerabilityDetails(value.Id);
            }
            this.RaiseAndSetIfChanged(ref _selectedVulnerability, value);
            
            ProcessStatusButtons();
        }
    }
    
    private List<CVEDetail> _cveDetails = new();
    public List<CVEDetail> CveDetails
    {
        get => _cveDetails;
        set => this.RaiseAndSetIfChanged(ref _cveDetails, value);
    }
    
    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set => this.RaiseAndSetIfChanged(ref _filterText, value);
    }
    
    private Host? _selectedVulnerabilityHost;

    public Host? SelectedVulnerabilityHost
    {
        get => _selectedVulnerabilityHost;
        private set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityHost, value);
    }
    
    private AuthenticatedUserInfo? _authenticatedUserInfo;

    public AuthenticatedUserInfo? AuthenticatedUserInfo
    {
        get => _authenticatedUserInfo;
        private set => this.RaiseAndSetIfChanged(ref _authenticatedUserInfo, value);
    }
    
    private Team? _selectedVulnerabilityFixTeam;

    public Team? SelectedVulnerabilityFixTeam
    {
        get => _selectedVulnerabilityFixTeam;
        private set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityFixTeam, value);
    }
    
    private UserDto? _selectedVulnerabilityAnalyst;

    public UserDto? SelectedVulnerabilityAnalyst
    {
        get => _selectedVulnerabilityAnalyst;
        private set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityAnalyst, value);
    }
    
    private ObservableCollection<Risk>? _selectedVulnerabilityRisks;

    private ObservableCollection<Risk>? SelectedVulnerabilityRisks
    {
        get => _selectedVulnerabilityRisks;
        set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityRisks, value);
    }
    
    private ObservableCollection<NrAction>? _selectedActions;
    public ObservableCollection<NrAction>? SelectedActions
    {
        get => _selectedActions;
        set => this.RaiseAndSetIfChanged(ref _selectedActions, value);
    }
    
    private ObservableCollection<Tuple<Risk,RiskScoring>>? _selectedRisksTuples;
    public ObservableCollection<Tuple<Risk,RiskScoring>>? SelectedRisksTuples
    {
        get => _selectedRisksTuples;
        set => this.RaiseAndSetIfChanged(ref _selectedRisksTuples, value);
    }
    
    private bool _btVerifyEnabled;
    public bool BtVerifyEnabled
    {
        get => _btVerifyEnabled;
        set => this.RaiseAndSetIfChanged(ref _btVerifyEnabled, value);
    }
    
    private bool _btRejectEnabled;
    public bool BtRejectEnabled
    {
        get => _btRejectEnabled;
        set => this.RaiseAndSetIfChanged(ref _btRejectEnabled, value);
    }
    
    private bool _btFixRequestedEnabled;
    public bool BtFixRequestedEnabled
    {
        get => _btFixRequestedEnabled;
        set => this.RaiseAndSetIfChanged(ref _btFixRequestedEnabled, value);
    }
    
    private bool _btCloseEnabled;
    public bool BtCloseEnabled
    {
        get => _btCloseEnabled;
        set => this.RaiseAndSetIfChanged(ref _btCloseEnabled, value);
    }
    
    private bool _btChatEnabled;
    public bool BtChatEnabled
    {
        get => _btChatEnabled;
        set => this.RaiseAndSetIfChanged(ref _btChatEnabled, value);
    }
    
    private int _page = 1;
    public int Page
    {
        get => _page;
        set => this.RaiseAndSetIfChanged(ref _page, value);
    }
    
    private bool _btPrioritizeEnabled;
    public bool BtPrioritizeEnabled
    {
        get => _btPrioritizeEnabled;
        set => this.RaiseAndSetIfChanged(ref _btPrioritizeEnabled, value);
    }

    private static Window? ParentWindow
    {
        get { return WindowsManager.AllWindows.Find(w => w is MainWindow); }
    }
    
    #endregion
    
    #region SERVICES
    
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    private IUsersService UsersService { get; } = GetService<IUsersService>();
    private IDialogService DialogService { get; } = GetService<IDialogService>();
    private IImpactsService ImpactsService { get; } = GetService<IImpactsService>();
    private IMutableConfigurationService MutableConfigurationService { get; } = GetService<IMutableConfigurationService>();
    private IFixRequestsService FixRequestsService { get; } = GetService<IFixRequestsService>();
    private IEmailsService EmailsService { get; } = GetService<IEmailsService>();
    
    #endregion

    #region BUTTONS

    public ReactiveCommand<Unit, Unit> BtReloadClicked { get; } 
    public ReactiveCommand<Unit, Unit> BtDetailsClicked { get; } 
    public ReactiveCommand<Unit, Unit> BtAddClicked { get; }
    public ReactiveCommand<Unit, Unit> BtEditClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteClicked { get; }
    public ReactiveCommand<Unit, Unit> BtVerifyClicked { get; }
    public ReactiveCommand<Unit, Unit> BtRejectClicked { get; }
    public ReactiveCommand<Unit, Unit> BtFixRequestClicked { get; }
    public ReactiveCommand<Unit, Unit> BtImportClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCloseClicked { get; }
    public ReactiveCommand<Unit, Unit> BtChatClicked { get; }
    public ReactiveCommand<Unit, Unit> BtPrioritizeClicked { get; }
    public ReactiveCommand<Unit, Unit> BtPageUpClicked { get; }
    public ReactiveCommand<Unit, Unit> BtPageDownClicked { get; }
    public ReactiveCommand<Unit, Unit> BtFilterShowClicked { get; }
    public ReactiveCommand<Unit, Unit> BtApplyFilterClicked { get; }

    #endregion

    #region FIELDS

    private bool _initialized;
    
    private int _totalRows;

    private const int PageSize = 100;

    #endregion
    
    #region CONSTRUCTOR
    public VulnerabilitiesViewModel()
    {
        DetailRotation = new RotateTransform(90);
        
        BtReloadClicked = ReactiveCommand.Create(ExecuteReload);
        BtDetailsClicked = ReactiveCommand.Create(ExecuteOpenCloseDetails);
        BtAddClicked = ReactiveCommand.Create(ExecuteAdd);
        BtDeleteClicked = ReactiveCommand.Create(ExecuteDelete);
        BtVerifyClicked = ReactiveCommand.Create(ExecuteVerify);
        BtEditClicked = ReactiveCommand.Create(ExecuteEdit);
        BtRejectClicked = ReactiveCommand.Create(ExecuteReject);
        BtFixRequestClicked = ReactiveCommand.Create(ExecuteFixRequest);
        BtImportClicked = ReactiveCommand.Create(ExecuteImport);
        BtCloseClicked = ReactiveCommand.Create(ExecuteClose);
        BtPrioritizeClicked = ReactiveCommand.Create(ExecutePrioritize);
        BtPageUpClicked = ReactiveCommand.Create(ExecutePageUp);
        
        BtChatClicked = ReactiveCommand.Create(ExecuteOpenChat);
        
        BtPageDownClicked = ReactiveCommand.Create(ExecutePageDown);
        BtApplyFilterClicked = ReactiveCommand.Create(() =>
        {
            MutableConfigurationService.SetConfigurationValue("vulnerabilityFilter", FilterText);
            Page = 1;
            ExecuteReload();
        });
        BtFilterShowClicked = ReactiveCommand.Create(() =>
        {
            FilterIsVisible = !FilterIsVisible;
        });
        
        AuthenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
        };
    }
    
    #endregion

    #region METHODS
    
    private async void Initialize()
    {
        if (!_initialized)
        {
            await Task.Run(() => {             
                UsersService.LoadCache();
                AuthenticatedUserInfo = AuthenticationService.AuthenticatedUserInfo;

                LoadData();
                
                Impacts = new ObservableCollection<LocalizableListItem>(ImpactsService.GetAll());
                
            });

                
            _initialized = true;
        }
    }
    
    private async void LoadData()
    {
        FilterText = MutableConfigurationService.GetConfigurationValue("vulnerabilityFilter") ?? "";

        try
        {
            var vulResult = await VulnerabilitiesService.GetFilteredAsync(PageSize, Page, FilterText, true);
            
            //var vulnerabilities = new ObservableCollection<Vulnerability>(
            //    VulnerabilitiesService.GetFiltered(PageSize, Page, FilterText, out _totalRows, out var validFilter));
            var vulnerabilities = new ObservableCollection<Vulnerability>(vulResult.Item1);
            _totalRows = vulResult.Item2;
            var validFilter = vulResult.Item3;
            

            if (validFilter)
            {
                Vulnerabilities = vulnerabilities;
                SelectedVulnerability = null;
            }
            else
            {
                FilterText = "";
                var vulResult2 = await VulnerabilitiesService.GetFilteredAsync(PageSize, Page, FilterText);
                Vulnerabilities = new ObservableCollection<Vulnerability>(vulResult2.Item1);
                _totalRows = vulResult2.Item2;
                //Vulnerabilities = new ObservableCollection<Vulnerability>(
                //    VulnerabilitiesService.GetFiltered(PageSize, Page, FilterText, out _totalRows, out validFilter));
            }

            RowCount = _totalRows;
        }
        catch (BadFilterException ex)
        {
            await Dispatcher.UIThread.Invoke(async () =>
            {
                var msgOk = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Alert"],
                        ContentMessage = Localizer["InvalidFilter"] + ": " + ex.Message,
                        Icon = Icon.Info,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgOk.ShowAsync();
            });

            
            FilterText = "";
            Vulnerabilities = new ObservableCollection<Vulnerability>(
                VulnerabilitiesService.GetFiltered(PageSize, Page, FilterText, out _totalRows,  out _));
        }

    }

    private async void ExecutePageUp()
    {
        if(_totalRows > PageSize * Page)
        {
            Page++;
            try
            {
                var vulnerabilities = new ObservableCollection<Vulnerability>(
                    VulnerabilitiesService.GetFiltered(PageSize, Page, FilterText, out _totalRows,
                        out var validFilter));

                if (validFilter)
                {
                    Vulnerabilities = vulnerabilities;
                    SelectedVulnerability = null;
                }
            }
            catch (BadFilterException ex)
            {
                var msgOk = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Alert"],
                        ContentMessage = Localizer["InvalidFilter"] + ": " + ex.Message,
                        Icon = Icon.Info,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgOk.ShowAsync();
            
                FilterText = "";
                Vulnerabilities = new ObservableCollection<Vulnerability>(
                    VulnerabilitiesService.GetFiltered(PageSize, Page, FilterText, out _totalRows,  out _));
            }

            
        }

    }
    
    private void ExecutePageDown()
    {
        try
        {
            if (Page > 1)
            {
                Page--;
                var vulnerabilities = new ObservableCollection<Vulnerability>(
                    VulnerabilitiesService.GetFiltered(PageSize, Page, FilterText, out _totalRows,
                        out var validFilter));

                if (validFilter)
                {
                    Vulnerabilities = vulnerabilities;
                    SelectedVulnerability = null;
                }
            }
        }
        catch (BadFilterException ex)
        {
            Log.Warning("Invalid filter message:{Message}", ex.Message);
            FilterText = "";
            Page--;
            var vulnerabilities = new ObservableCollection<Vulnerability>(
                VulnerabilitiesService.GetFiltered(PageSize, Page, FilterText, out _totalRows,
                    out _));
            
            Vulnerabilities = vulnerabilities;
            SelectedVulnerability = null;
        }

    }
    private async void ExecuteAdd()
    {
        var parameter = new VulnerabilityDialogParameter()
        {
            Operation = OperationType.Create
        };
        
        var dialogNewVul = await DialogService.ShowDialogAsync<VulnerabilityDialogResult, VulnerabilityDialogParameter>(nameof(EditVulnerabilitiesDialogViewModel), parameter);
        
        if(dialogNewVul == null) return;

        if (dialogNewVul.Action == ResultActions.Ok )
        {
            Vulnerabilities.Add(dialogNewVul.ResultingVulnerability!);
        }
    }

    private void ExecutePrioritize()
    {
        var user = AuthenticationService.AuthenticatedUserInfo!.UserName;
        
        var nrAction = new NrAction()
        {
            DateTime = DateTime.Now,
            Id = 0,
            Message = "PRIORITIZED BY: " + user,
            UserId = AuthenticationService.AuthenticatedUserInfo!.UserId,
            ObjectType = nameof(Vulnerability),
        };
        
        
        VulnerabilitiesService.UpdateStatus(SelectedVulnerability!.Id, (ushort) IntStatus.Prioritized);
        VulnerabilitiesService.AddActionAsync(SelectedVulnerability!.Id, nrAction.UserId!.Value, nrAction);
        var idx = Vulnerabilities.IndexOf(SelectedVulnerability);
        Vulnerabilities[idx].Status = (ushort) IntStatus.Prioritized;

        var vulnerabilities = Vulnerabilities;
        var selected = SelectedVulnerability;
        Vulnerabilities = new ObservableCollection<Vulnerability>();
        Vulnerabilities = vulnerabilities;
        SelectedVulnerability = selected;
        ProcessStatusButtons();
    }
    
    private async void ExecuteImport()
    {
        var importWindow = new VulnerabilityImportWindow();
        var importViewModel = new VulnerabilityImportViewModel
        {
            ParentWindow = importWindow
        };

        importWindow.DataContext = importViewModel;
        
        await importWindow.ShowDialog(ParentWindow!);
        ExecuteReload();
    }

    private async void ExecuteEdit()
    {
        
        if(SelectedVulnerability == null)
        {
            var msgOk = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Alert"],
                    ContentMessage = Localizer["PleaseSelectAVulnerabilityMSG"],
                    Icon = Icon.Info,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgOk.ShowAsync();
            
            return;
        }
        var parameter = new VulnerabilityDialogParameter()
        {
            Operation = OperationType.Edit,
            Vulnerability = SelectedVulnerability
        };
        
        var editedVul = await 
            DialogService.ShowDialogAsync<VulnerabilityDialogResult, VulnerabilityDialogParameter>
                (nameof(EditVulnerabilitiesDialogViewModel), parameter);
        
        if(editedVul == null) return;
        
        if (editedVul.Action == ResultActions.Ok )
        {
            var idx = Vulnerabilities.IndexOf(SelectedVulnerability);
            Vulnerabilities[idx] = editedVul.ResultingVulnerability!;
            SelectedVulnerability = Vulnerabilities[idx];
        }
        
    }
    
    private async void ExecuteDelete()
    {
        if(SelectedVulnerability == null)
        {
            var msgOk = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Alert"],
                    ContentMessage = Localizer["PleaseSelectAVulnerabilityMSG"],
                    Icon = Icon.Info,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgOk.ShowAsync();
            
        }
        else
        {
            var msgConfirm = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Alert"],
                    ContentMessage = Localizer["AreYouSureToDeleteVulnerabilityMSG"] + " " + SelectedVulnerability.Title,
                    Icon = Icon.Question,
                    ButtonDefinitions = ButtonEnum.YesNoAbort,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });
            var confirmMsg = await msgConfirm.ShowWindowDialogAsync(ParentWindow);
            
            if (confirmMsg == ButtonResult.Yes)
            {
                try
                {
                    VulnerabilitiesService.Delete(SelectedVulnerability);
                    Vulnerabilities.Remove(SelectedVulnerability);
                }
                catch (Exception ex)
                {
                    var msgOk = MessageBoxManager
                        .GetMessageBoxStandard(new MessageBoxStandardParams
                        {
                            ContentTitle = Localizer["Error"],
                            ContentMessage = Localizer["ErrorDeletingVulnerabilityMSG"] + " " + ex.Message,
                            Icon = Icon.Error,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        });

                    await msgOk.ShowAsync();
                }
                
            }
        }
    }

    private void ExecuteVerify()
    {
        //check if the vulnerability has a risk associated to it
        if (SelectedVulnerability!.Risks.Count == 0)
        {
            var msgOk = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Alert"],
                    ContentMessage = Localizer["PleaseAddARiskToTheVulnerabilityMSG"],
                    Icon = Icon.Info,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            msgOk.ShowAsync();
            
            return;
        }
        
        var user = AuthenticationService.AuthenticatedUserInfo!.UserName;
        
        var nrAction = new NrAction()
        {
            DateTime = DateTime.Now,
            Id = 0,
            Message = "VERIFIED BY: " + user,
            UserId = AuthenticationService.AuthenticatedUserInfo!.UserId,
            ObjectType = nameof(Vulnerability),
        };
        
        
        VulnerabilitiesService.UpdateStatus(SelectedVulnerability!.Id, (ushort) IntStatus.Verified);
        VulnerabilitiesService.AddActionAsync(SelectedVulnerability!.Id, nrAction.UserId!.Value, nrAction);
        var idx = Vulnerabilities.IndexOf(SelectedVulnerability);
        Vulnerabilities[idx].Status = (ushort) IntStatus.Verified;

        var vulnerabilities = Vulnerabilities;
        var selected = SelectedVulnerability;
        Vulnerabilities = new ();
        Vulnerabilities = vulnerabilities;
        SelectedVulnerability = selected;
        ProcessStatusButtons();
    }

    private async void ExecuteOpenChat()
    {

        if (SelectedVulnerability.FixRequests.Count == 0) return;
        
        var parameter = new VulnerabilityFixChatDialogParameter()
        {
            VulnerabilityId = SelectedVulnerability!.Id,
            FixRequestId = SelectedVulnerability.FixRequests.Last().Id
        };
        
        var dialogChat = await DialogService.ShowDialogAsync<VulnerabilityFixChatDialogResult, VulnerabilityFixChatDialogParameter>(nameof(VulnerabilityFixChatDialogViewModel), parameter);
        
        //if(dialogChat == null) return;
        //if(dialogChat!.Action != ResultActions.Send) return;
        
    }

    private async void ExecuteReject()
    {
        
        var parameter = new StringDialogParameter()
        {
            Title = Localizer["ReasonForRejection"],
            FieldName = Localizer["Reason"]
        };
        
        var dialogReject = await DialogService.ShowDialogAsync<StringDialogResult, StringDialogParameter>(nameof(EditSingleStringDialogViewModel), parameter);
        
        if(dialogReject == null) return;

        if (dialogReject.Action != ResultActions.Ok) return;

        var reason = dialogReject.Result;
        
        var user = AuthenticationService.AuthenticatedUserInfo!.UserName;
        
        var nrAction = new NrAction()
        {
            DateTime = DateTime.Now,
            Id = 0,
            Message = "REJECTED BY: " + user + "\n---\n" + reason,
            UserId = AuthenticationService.AuthenticatedUserInfo!.UserId,
            ObjectType = nameof(Vulnerability),
        };
        
        
        VulnerabilitiesService.UpdateStatus(SelectedVulnerability!.Id, (ushort) IntStatus.Rejected);
        await VulnerabilitiesService.AddActionAsync(SelectedVulnerability!.Id, nrAction.UserId!.Value, nrAction);
        var idx = Vulnerabilities.IndexOf(SelectedVulnerability);
        Vulnerabilities[idx].Status = (ushort) IntStatus.Rejected;

        var vulnerabilities = Vulnerabilities;
        var selected = SelectedVulnerability;
        Vulnerabilities = new();
        Vulnerabilities = vulnerabilities;
        SelectedVulnerability = selected;
        ProcessStatusButtons();
    }

    private async void ExecuteFixRequest()
    {
        if(SelectedVulnerability == null) return;
        if(SelectedVulnerability.Score == null) SelectedVulnerability.Score = 0;
        
        double originalDouble = SelectedVulnerability.Score.Value;
        double truncatedDouble = Math.Truncate(originalDouble * 100) / 100;
        float truncatedFloat = (float)truncatedDouble;
        
        
        var parameter = new FixRequestDialogParameter()
        {
            Vulnerability = SelectedVulnerability!.Title,
            Score = truncatedFloat,
            Solution = SelectedVulnerability.Solution,
            Comments = SelectedVulnerability.Comments,
            FixTeamId = SelectedVulnerability.FixTeamId
        };
        
        var dialogFix = await DialogService.ShowDialogAsync<FixRequestDialogResult, FixRequestDialogParameter>(nameof(FixRequestDialogViewModel), parameter);
        
        if(dialogFix == null) return;
        if(dialogFix!.Action != ResultActions.Send) return;
        
        
        var user = AuthenticationService.AuthenticatedUserInfo!.UserName;
        
        //Creating the fix request

        var destination = "";
        
        if (dialogFix.FixTeamId == null) destination = dialogFix.SendTo;
        
        
        var fixRequest = new FixRequestDto()
        {
            Comments = dialogFix.Comments,
            VulnerabilityId = SelectedVulnerability.Id,
            FixTeamId = dialogFix.FixTeamId,
            Destination = destination
        };
        
        
        var fixRequestCreated = await FixRequestsService.CreateFixRequestAsync(fixRequest);
        
        // Adding the action to the fixRequest
        
        
        var nrAction = new NrAction()
        {
            DateTime = DateTime.Now,
            Id = 0,
            Message = "FIX REQUESTED BY: " + user,
            UserId = AuthenticationService.AuthenticatedUserInfo!.UserId,
            ObjectType = nameof(Vulnerability),
        };
        
        // Adding comments to the fixRequest
        
        SelectedVulnerability.Comments = dialogFix.Comments;
        
        SelectedVulnerability.FixTeamId = dialogFix.FixTeamId;
        
        
        VulnerabilitiesService.Update(SelectedVulnerability);
        
        SelectedVulnerability.FixRequests.Add(fixRequestCreated);
        

        var sendToGroup = false;
        if (dialogFix.FixTeamId != null) sendToGroup = true;
        
        await EmailsService.SendVulnerabilityFixRequestMailAsync(fixRequest, sendToGroup);
        
        VulnerabilitiesService.UpdateStatus(SelectedVulnerability!.Id, (ushort) IntStatus.AwaitingFix);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        VulnerabilitiesService.AddActionAsync(SelectedVulnerability!.Id, nrAction.UserId!.Value, nrAction);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        
        var idx = Vulnerabilities.IndexOf(SelectedVulnerability);
        Vulnerabilities[idx].Status = (ushort) IntStatus.AwaitingFix;
        
        

        var vulnerabilities = Vulnerabilities;
        var selected = SelectedVulnerability;
        Vulnerabilities = new ObservableCollection<Vulnerability>();
        Vulnerabilities = vulnerabilities;
        SelectedVulnerability = selected;
        ProcessStatusButtons();
    }

    private async void ExecuteClose()
    {

        var parameters = new CloseDialogParameter();
        
        var closeDialog = await DialogService.ShowDialogAsync<CloseDialogResult, CloseDialogParameter>(nameof(CloseDialogViewModel), parameters);
        
        if(closeDialog == null) return;

        if (closeDialog.Action != ResultActions.Ok) return;
        var user = AuthenticationService.AuthenticatedUserInfo!.UserName;
        
        var nrAction = new NrAction()
        {
            DateTime = DateTime.Now,
            Id = 0,
            Message = "CLOSED BY: " + user + "\n" +
                      "Final Status: " + closeDialog.FinalStatus.ToString(),
            UserId = AuthenticationService.AuthenticatedUserInfo!.UserId,
            ObjectType = nameof(Vulnerability),
        };

        SelectedVulnerability.Comments += "\n---\n" + closeDialog.Comments;


        switch (closeDialog.FinalStatus)
        {
            case IntStatus.Fixed:
                SelectedVulnerability.Solution = "Fixed";
                break;
            default:
                SelectedVulnerability.Solution = "Not Fixed";
                break;
        }
        
        VulnerabilitiesService.UpdateCommentsAsync(SelectedVulnerability!.Id, SelectedVulnerability.Comments);
        
        VulnerabilitiesService.UpdateStatus(SelectedVulnerability!.Id, (ushort) closeDialog.FinalStatus);
        await VulnerabilitiesService.AddActionAsync(SelectedVulnerability!.Id, nrAction.UserId!.Value, nrAction);
        
        var idx = Vulnerabilities.IndexOf(SelectedVulnerability);
        Vulnerabilities[idx].Status = (ushort) closeDialog.FinalStatus;

        var vulnerabilities = Vulnerabilities;
        var selected = SelectedVulnerability;
        Vulnerabilities = new ();
        Vulnerabilities = vulnerabilities;
        SelectedVulnerability = selected;
        ProcessStatusButtons();
    }

    private void BlockAllStatusButtons()
    {
        BtVerifyEnabled = false;
        BtRejectEnabled = false;
        BtFixRequestedEnabled = false;
        BtCloseEnabled = false;
        BtPrioritizeEnabled = false;
        BtChatEnabled = false;
    }
    private void ProcessStatusButtons()
    {
        if(SelectedVulnerability == null)
        {
            BlockAllStatusButtons();
        }
        else
        {
            switch (SelectedVulnerability.Status)
            {
                case (ushort) IntStatus.New:
                    BlockAllStatusButtons();
                    BtVerifyEnabled = true;
                    BtRejectEnabled = true;
                    BtCloseEnabled = true;
                    break;
                case (ushort) IntStatus.Verified:
                    BlockAllStatusButtons();
                    BtRejectEnabled = true;
                    BtFixRequestedEnabled = true;
                    break;
                case (ushort) IntStatus.AwaitingFix:
                    BlockAllStatusButtons();
                    BtVerifyEnabled = true;
                    BtCloseEnabled = true;
                    BtPrioritizeEnabled = true;
                    BtChatEnabled = true;
                    break;
                case (ushort) IntStatus.Prioritized:
                    BlockAllStatusButtons();
                    BtVerifyEnabled = true;
                    BtCloseEnabled = true;
                    BtFixRequestedEnabled = true;
                    BtChatEnabled = true;
                    break;
                default:
                    BlockAllStatusButtons();
                    break;
            }
            
            
        }
    }
    
    private void ExecuteReload()
    {
        LoadData();
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

    public void OpenUrl(object urlObj)
    {
        var url = urlObj as string;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            //https://stackoverflow.com/a/2796367/241446
            using var proc = new Process();
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.FileName = url;
            proc.Start();

            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if(url != null)
                Process.Start("x-www-browser", url);
            return;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) throw new ArgumentException("invalid url: " + url);
        Process.Start("open", "-u " + url);
    }
    
    private async void LoadVulnerabilityDetails(int vulnerabilityId)
    {
        var vulnerability = await VulnerabilitiesService.GetOneAsync(vulnerabilityId);
        
        SelectedVulnerabilityHost = vulnerability.Host;
        SelectedVulnerabilityFixTeam = vulnerability.FixTeam;
        SelectedActions = new ObservableCollection<NrAction>(vulnerability.Actions);
        if(vulnerability.AnalystId != null)
            SelectedVulnerabilityAnalyst = await UsersService.GetUserAsync(vulnerability.AnalystId.Value);
        SelectedVulnerabilityRisks = new ObservableCollection<Risk>(vulnerability.Risks);
        SelectedVulnerabilityRisksScores = new ObservableCollection<RiskScoring>(await VulnerabilitiesService.GetRisksScoresAsync(vulnerabilityId));

        SelectedRisksTuples = new ObservableCollection<Tuple<Risk, RiskScoring>>();

        if (!String.IsNullOrEmpty(vulnerability.Cves))
        {
            var cves = vulnerability.Cves.Split(",");
            CveDetails = new List<CVEDetail>();
            foreach (var cve in cves)
            {
                if(cve != string.Empty && cve != " ") CveDetails.Add(new CVEDetail() {Id = cve});
            }
        }
        
        foreach (var risk in SelectedVulnerabilityRisks)
        {
            RiskScoring rs;
            if (SelectedVulnerabilityRisksScores == null || SelectedVulnerabilityRisksScores.Count == 0 || SelectedVulnerabilityRisksScores.FirstOrDefault(r => r.Id == risk.Id) == null)
            {
                rs = new RiskScoring()
                {
                    Id = risk.Id
                };
            }
            else
            {
                rs = SelectedVulnerabilityRisksScores.First(r => r.Id == risk.Id);
            }
            SelectedRisksTuples.Add(new Tuple<Risk, RiskScoring>(risk, rs ));
        }
        
    }

    #endregion
}