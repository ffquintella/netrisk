using System.Collections.ObjectModel;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class EditVulnerabilitiesDialogViewModel: ParameterizedDialogViewModelBaseAsync<VulnerabilityDialogResult, VulnerabilityDialogParameter>
{
    #region LANGUAGE
        public string StrVulnerability { get; } = Localizer["Vulnerability"];
        public string StrTile { get; } = Localizer["Title"];
        public string StrScore { get; } = Localizer["Score"];
        public string StrImpact { get; } = Localizer["Impact"];
        public string StrLow { get; } = Localizer["Low"];
        public string StrMedium { get; } = Localizer["Medium"];
        public string StrHigh { get; } = Localizer["High"];
        
        public string StrTechnologies { get; } = Localizer["Technologies"];
    #endregion
    
    #region PROPERTIES

    private string? _strOperation = null;
    public string? StrOperation
    {
        get => _strOperation;
        set => this.RaiseAndSetIfChanged(ref _strOperation, value);
    }
    
    private OperationType _operation;
    private OperationType Operation
    {
        get => _operation;
        set
        {
            if(value == OperationType.Create) StrOperation = Localizer["Create"];
            else if(value == OperationType.Edit) StrOperation = Localizer["Edit"];
            this.RaiseAndSetIfChanged(ref _operation, value);
        }
    }
    
    private ObservableCollection<Technology> _technologies = new();
    public ObservableCollection<Technology> Technologies
    {
        get => _technologies;
        set => this.RaiseAndSetIfChanged(ref _technologies, value);
    }

    #endregion
    
    #region BUTTONS
    #endregion

    #region FIELDS
    
    #endregion
    
    #region SERVICES

    private ITechnologiesService TechnologiesService { get; } = GetService<ITechnologiesService>();
    #endregion
    
    public override Task ActivateAsync(VulnerabilityDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Operation = parameter.Operation;
            
            Technologies = new ObservableCollection<Technology>(TechnologiesService.GetAll());
            
            /*if(parameter.Title != null) StrTitle = parameter.Title;
            if(parameter.FieldName != null) StrFieldName = parameter.FieldName;
            */
        });
    }
}