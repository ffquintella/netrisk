using System;
using DAL.Entities;
using GUIClient.Models;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class EditIncidentViewModel: ViewModelBase
{

    #region LANGUAGE

    #endregion

    #region FIELDS

    #endregion
    
    #region PROPERTIES
    
    private OperationType WindowOperationType { get; set; } 
    
    private Incident _incident;
    
    public Incident Incident
    {
        get => _incident;
        set => this.RaiseAndSetIfChanged(ref _incident, value);
    }
    
    #endregion
    
    #region SERVICES
    
    #endregion
    
    #region COMMANDS
    
    #endregion
    
    #region CONSTRUCTOR
    
    public EditIncidentViewModel(OperationType operationType, Incident? incident = null)
    {
        WindowOperationType = operationType;

        Incident = WindowOperationType switch
        {
            OperationType.Edit => incident ?? throw new ArgumentNullException(nameof(incident)),
            OperationType.Create => new Incident(),
            _ => throw new Exception("Invalid operation type")
        };
        
        
    }
    
    #endregion
    
    #region METHODS
    
    #endregion
    
}