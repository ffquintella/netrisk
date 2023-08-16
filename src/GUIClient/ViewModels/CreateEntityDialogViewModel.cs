using System.Collections.Generic;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Entities;

namespace GUIClient.ViewModels;

public class CreateEntityDialogViewModel: DialogViewModelBase<IntegerDialogResult>
{
    #region LANGUAGE

        public string StrTitle { get; set; }
        public string StrName { get; set; }
        public string StrParent{ get; set; }
        public string StrSave{ get; set; }
        public string StrCancel{ get; set; }
        public string StrType { get; set; }

    #endregion

    #region PROPERTIES

        public IntegerDialogResult Result { get; set; }
        public string Name { get; set; }

        public List<string> EntityTypes { get; set; } = new List<string>()
        {
            "---", "a", "b", "c"
        };

    #endregion
    
    public CreateEntityDialogViewModel() : base()
    {
        StrTitle = Localizer["CreateEntity"];
        StrName = Localizer["Name"] + ":";
        StrParent = Localizer["Parent"] + ":";
        StrSave = Localizer["Save"];
        StrCancel = Localizer["Cancel"];
        StrType = Localizer["Type"] + ":";
            
        this.Result = new IntegerDialogResult();
        
    }
}