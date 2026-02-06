using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model;
using ReactiveUI;
using System.Reactive;
using GUIClient.Views;

namespace GUIClient.ViewModels;

public class CloseDialogViewModel: ParameterizedDialogViewModelBaseAsync<CloseDialogResult, CloseDialogParameter>
{
    #region LANGUAGE

    public string StrTitle { get; set; } = Localizer["CloseDialog"];
    public string StrFinalStatus { get; } = Localizer["FinalStatus"];
    public string StrComments { get; }= Localizer["Comments"];

    

    #endregion
    
    #region PROPERTIES
    
        private string _comments = "";
        public string Comments
        {
            get => _comments;
            set => this.RaiseAndSetIfChanged(ref _comments, value);
        }
        
        private IntStatus _finalStatus = IntStatus.Closed;

        public IntStatus FinalStatus
        {
            get => _finalStatus;
            set => this.RaiseAndSetIfChanged(ref _finalStatus, value);
        }
        
        public List<IntStatus> Statuses { get; } = new()
        {
            IntStatus.Closed,
            IntStatus.Fixed,
            IntStatus.Mitigated,
            IntStatus.NotRelevant,
            IntStatus.Rejected,
            IntStatus.Retired,
            IntStatus.Duplicated,
            IntStatus.Outdated,
            IntStatus.Deleted,
        };
        
        private bool _isSaveEnabled = true;
        public bool IsSaveEnabled
        {
            get => _isSaveEnabled;
            set => this.RaiseAndSetIfChanged(ref _isSaveEnabled, value);
        }
        
        
    
    #endregion

    #region BUTTONS
    public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCancelClicked { get; }
    

    #endregion
    
    public CloseDialogViewModel()
    {
        BtSaveClicked = ReactiveCommand.Create(ExecuteSave);
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);
        
    }


    #region METHODS

    private void ExecuteSave()
    {
        var result = new CloseDialogResult()
        {
            Action = ResultActions.Ok
        };
        result.Comments = Comments;
        result.FinalStatus = FinalStatus;
        Close(result);
    }
    
    private void ExecuteCancel()
    {
        Close(new CloseDialogResult()
        {
            Action = ResultActions.Cancel
        });
    }

    public override Task ActivateAsync(CloseDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if(parameter.Title != null) StrTitle = parameter.Title;
            Comments = parameter.Comments;

        });
    }

    #endregion
    

}