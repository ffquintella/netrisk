using DAL.Entities;
using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

public class AssessmentsRunsListViewModel: ViewModelBase
{
    #region LANGUAGE
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

    #endregion
    
    #region CONSTRUCTORS

    public AssessmentsRunsListViewModel()
    {
        
    }
    
    public AssessmentsRunsListViewModel(Assessment assessment)
    {
        Assessment = assessment;
    }
    #endregion
    
    #region METHODS
    #endregion
}