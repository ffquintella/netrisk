using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
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

    #endregion
    
    #region BUTTONS
    #endregion

    #region FIELDS
    
    #endregion
    
    #region SERVICES
    #endregion
    
    public override Task ActivateAsync(VulnerabilityDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Operation = parameter.Operation;
            
            /*if(parameter.Title != null) StrTitle = parameter.Title;
            if(parameter.FieldName != null) StrFieldName = parameter.FieldName;
            */
        });
    }
}