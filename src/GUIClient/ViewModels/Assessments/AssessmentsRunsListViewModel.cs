using System.Collections.ObjectModel;
using ClientServices.Interfaces;
using DAL.Entities;
using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

public class AssessmentsRunsListViewModel: ViewModelBase
{
    #region LANGUAGE
        private string StrDate => Localizer["Date"];
        private string StrAnalyst => Localizer["Analyst"];
        private string StrEntity => Localizer["Entity"];
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
        set => this.RaiseAndSetIfChanged(ref _selectedAssessmentRun, value);
    }

    #endregion
    
    #region SERVICES

    private IAssessmentsService AssessmentsService { get; } = GetService<IAssessmentsService>();
    
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
    
    #endregion
}