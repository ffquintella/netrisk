using System.Collections.ObjectModel;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;

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
        set => this.RaiseAndSetIfChanged(ref _assessment, value);
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
            this.RaiseAndSetIfChanged(ref _selectedAssessmentRun, value);
            LoadAssessmentRunsAnswers();
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
        //TODO Edit result 
        /*if(runResult != null &&  runResult.Action == ResultActions.Ok)
        {
            AssessmentRuns.Add(runResult.CreatedAssessmentRun!);
        }*/
    }
    
    public void DeleteAssessmentRunCommand()
    {
        
    }
    
    #endregion
}