using System.Threading;
using System.Threading.Tasks;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using System.Reactive;


namespace GUIClient.ViewModels;

public class EditSingleStringDialogViewModel: ParameterizedDialogViewModelBaseAsync<StringDialogResult, StringDialogParameter>
{
    
    private string _strTitle = "";
    public string StrTitle
    {
        get => _strTitle;
        set => this.RaiseAndSetIfChanged(ref _strTitle, value);
    } 

    private string _strFieldName = "";
    public string StrFieldName
    {
        get => _strFieldName;
        set => this.RaiseAndSetIfChanged(ref _strFieldName, value);
    } 
    
    private string _dialogValue = "";
    public string DialogValue
    {
        get => _dialogValue;
        set
        {
            if(value.Length > 0) SaveEnabled = true;
            else SaveEnabled = false;
            this.RaiseAndSetIfChanged(ref _dialogValue, value);
        }
    }

    private bool _saveEnabled; 
    
    public bool SaveEnabled
    {
        get => _saveEnabled;
        set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
    }
    
    public string StrSave { get; } 
    public string StrCancel { get; } 

    public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCancelClicked { get; }
    
    public EditSingleStringDialogViewModel()
    {
        StrSave = Localizer["Save"];
        StrCancel = Localizer["Cancel"];
        
        BtSaveClicked = ReactiveCommand.Create(ExecuteSave);
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);
        
        this.ValidationRule(
            viewModel => viewModel.DialogValue, 
            value => value is {Length: > 0},
            Localizer["PleaseEnterAValueMSG"]);
        
        /*this.IsValid()
            .Subscribe( xc =>
            {
                SaveEnabled = xc;
            });*/
    }
    
    private void ExecuteSave()
    {
        var result = new StringDialogResult
        {
            Result = DialogValue,
            Action = ResultActions.Ok
        };
        Close(result);
    }
    
    private void ExecuteCancel()
    {
        var result = new StringDialogResult
        {
            Result = DialogValue,
            Action = ResultActions.Cancel
        };
        Close(result);
    }
    
    public override Task ActivateAsync(StringDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if(parameter.Title != null) StrTitle = parameter.Title;
            if(parameter.FieldName != null) StrFieldName = parameter.FieldName;
            
        });
    }
}