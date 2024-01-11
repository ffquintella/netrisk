using System;
using System.Collections.ObjectModel;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Assessments;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Serilog;

namespace GUIClient.ViewModels.Assessments;

public class AssessmentsRunsListViewModel: ViewModelBase
{
    #region LANGUAGE
        private string StrDate => Localizer["Date"];
        private string StrAnalyst => Localizer["Analyst"];
        private string StrEntity => Localizer["Entity"];
        private string StrAnswer => Localizer["Answer"];
        private string StrQuestion => Localizer["Question"];
    #endregion
    
    #region FIELDS
    #endregion

    #region PROPERTIES

    private Assessment? _assessment;
    public Assessment? Assessment
    {
        get => _assessment;
        set
        {
            if (value == null)
            {
                CanAdd = false;
                CanDelete = false;
            }
            else
            {
                CanAdd = true;
                CanDelete = true;
                
            }
            this.RaiseAndSetIfChanged(ref _assessment, value);  
        } 
    }
    
    private ObservableCollection<AssessmentRun> _assessmentRuns = new();
    public ObservableCollection<AssessmentRun> AssessmentRuns
    {
        get => _assessmentRuns;
        set => this.RaiseAndSetIfChanged(ref _assessmentRuns, value);
    }
    
    private AssessmentRun? _selectedAssessmentRun;
    public AssessmentRun? SelectedAssessmentRun
    {
        get => _selectedAssessmentRun;
        set
        {
            if(value == null)
            {
                CanEdit = false;
            }
            else
            {
                if (value.Status == (int) AssessmentStatus.Submitted)
                {
                    Submited = true;
                    CanEdit = false;
                    CanAdd = false;
                    CanDelete = false;
                }
                else
                {
                    Submited = false;
                    CanEdit = true;
                }
                
            }
            this.RaiseAndSetIfChanged(ref _selectedAssessmentRun, value);
            LoadAssessmentRunsAnswers();
        }
    }
    
    public bool _submited = false;
    public bool Submited
    {
        get => _submited;
        set => this.RaiseAndSetIfChanged(ref _submited, value);
    }
    
    public bool _canAdd = false;
    public bool CanAdd
    {
        get => _canAdd;
        set
        {
            if (value && Submited) return;
            this.RaiseAndSetIfChanged(ref _canAdd, value);
        }
    }

    public bool _canEdit = false;
    public bool CanEdit
    {
        get => _canEdit;
        set
        {
            if (value && Submited) return;
            this.RaiseAndSetIfChanged(ref _canEdit, value);
        }
    }

    public bool _canDelete = false;
    public bool CanDelete
    {
        get => _canDelete;
        set
        {
            if (value && Submited) return;
            this.RaiseAndSetIfChanged(ref _canDelete, value);
        }
    }


    private ObservableCollection<AssessmentRunsAnswer> _assessmentRunsAnswers = new();
    public ObservableCollection<AssessmentRunsAnswer> AssessmentRunsAnswers
    {
        get => _assessmentRunsAnswers;
        set
        {
            this.RaiseAndSetIfChanged(ref _assessmentRunsAnswers, value);
        }
    }

    private AssessmentRunsAnswer? _selectedAssessmentRunsAnswer;
    public AssessmentRunsAnswer? SelectedAssessmentRunsAnswer
    {
        get => _selectedAssessmentRunsAnswer;
        set => this.RaiseAndSetIfChanged(ref _selectedAssessmentRunsAnswer, value);
    }
    
    #endregion
    
    #region SERVICES

    private IAssessmentsService AssessmentsService { get; } = GetService<IAssessmentsService>();
    private IDialogService DialogService { get; } = GetService<IDialogService>();
    
    #endregion
    
    #region CONSTRUCTORS

    public AssessmentsRunsListViewModel()
    {
        
    }
    
    public AssessmentsRunsListViewModel(Assessment assessment)
    {
        Assessment = assessment;
        
        LoadAssessmentRuns();
    }
    #endregion
    
    #region METHODS
    
    private void LoadAssessmentRuns()
    {
        if (Assessment is null) return;
        
        var runs = AssessmentsService.GetAssessmentRuns(Assessment.Id);
        AssessmentRuns = new ObservableCollection<AssessmentRun>(runs);
    }
    
    private void LoadAssessmentRunsAnswers()
    {
        if (SelectedAssessmentRun is null) return;
        
        var answers = AssessmentsService.GetAssessmentRunAnsers(Assessment.Id, SelectedAssessmentRun.Id);
        AssessmentRunsAnswers = new ObservableCollection<AssessmentRunsAnswer>(answers);
    }

    public async void AddAssessmentRunCommand()
    {
        var parameter = new AssessmentRunDialogParameter()
        {
            Operation = OperationType.Create,
            Assessment = Assessment
        };
        
        var runResult = await DialogService.ShowDialogAsync<AssessmentRunDialogResult, AssessmentRunDialogParameter>(nameof(AssessmentRunDialogViewModel), parameter);
        if(runResult != null &&  runResult.Action == ResultActions.Ok)
        {
            AssessmentRuns.Add(runResult.CreatedAssessmentRun!);
        }
    }

    public async void EditAssessmentRunCommand()
    {
        var parameter = new AssessmentRunDialogParameter()
        {
            Operation = OperationType.Edit,
            Assessment = Assessment,
            AssessmentRun = SelectedAssessmentRun
        };
        
        var runResult = await DialogService.ShowDialogAsync<AssessmentRunDialogResult, AssessmentRunDialogParameter>(nameof(AssessmentRunDialogViewModel), parameter);

        if(runResult != null &&  (runResult.Action == ResultActions.Ok|| runResult.Action == ResultActions.Submitted))
        {
            LoadAssessmentRuns();
            LoadAssessmentRunsAnswers();
        }
    }
    
    public async void DeleteAssessmentRunCommand()
    {
        if(Assessment is null) return;
        if(SelectedAssessmentRun is null) return;
        try
        {
            var messageBoxConfirm = MessageBoxManager
                .GetMessageBoxStandard(  new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["RunDeleteConfirmationMSG"]  ,
                    ButtonDefinitions = ButtonEnum.OkAbort,
                    Icon = Icon.Question,
                });
                        
            var confirmation = await messageBoxConfirm.ShowAsync();

            if (confirmation == ButtonResult.Ok)
            {
                AssessmentsService.DeleteRun(Assessment.Id, SelectedAssessmentRun.Id);
                AssessmentRuns.Remove(SelectedAssessmentRun);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Error deleting assessment run");
        }
    }
    
    #endregion
}