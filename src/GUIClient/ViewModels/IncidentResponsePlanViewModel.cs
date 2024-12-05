using System.Linq;
using DAL.Entities;
using GUIClient.Models;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class IncidentResponsePlanViewModel: ViewModelBase
{
    #region LANGUAGE
        private string StrTitle => Localizer["Incident Response Plan"];
        private string StrRisk => Localizer["Risk"];
        private string StrName => Localizer["Name"];
        private string StrPlan => Localizer["Plan"];
    #endregion
    
    #region FIELDS
    #endregion
    
    #region PROPERTIES

    private IncidentResponsePlan? _incidentResponsePlan;
    public IncidentResponsePlan? IncidentResponsePlan
    {
        get => _incidentResponsePlan;
        set => this.RaiseAndSetIfChanged(ref _incidentResponsePlan, value);
    }
    
    private Risk? _relatedRisk;
    
    public Risk? RelatedRisk
    {
        get => _relatedRisk;
        set => this.RaiseAndSetIfChanged(ref _relatedRisk, value);
    }
    
    private OperationType _windowOperationType;
    
    public OperationType WindowOperationType
    {
        get => _windowOperationType;
        set => this.RaiseAndSetIfChanged(ref _windowOperationType, value);
    }
    
    public bool IsCreateOperation => WindowOperationType == OperationType.Create;
    public bool IsEditOperation => WindowOperationType == OperationType.Edit;
    
    private string _name = "";
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }
    
    #endregion

    #region SERVICES
    #endregion

    #region CONSTRUCTOR
    
    /// <summary>
    /// Create a new instance of IncidentResponsePlanViewModel on edit operation
    /// </summary>
    /// <param name="incidentResponsePlan"></param>
    /// <param name="relatedRisk"></param>
    public IncidentResponsePlanViewModel(IncidentResponsePlan incidentResponsePlan, Risk relatedRisk)
    {
        IncidentResponsePlan = incidentResponsePlan;
        WindowOperationType = OperationType.Edit;
        RelatedRisk = relatedRisk;
    }
    
    /// <summary>
    /// Create a new instance of IncidentResponsePlanViewModel on create operation
    /// </summary>
    /// <param name="relatedRisk"></param>
    public IncidentResponsePlanViewModel(Risk relatedRisk)
    {
        RelatedRisk = relatedRisk;
        WindowOperationType = OperationType.Create;
        IncidentResponsePlan = new IncidentResponsePlan();
    }
    #endregion
    
    #region METHODS
    #endregion
}