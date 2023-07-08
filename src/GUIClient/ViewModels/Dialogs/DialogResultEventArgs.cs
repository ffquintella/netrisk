using System;

namespace GUIClient.ViewModels.Dialogs;

public class DialogResultEventArgs<TResult> : EventArgs
{
    public TResult? Result { get; }

    public DialogResultEventArgs(TResult? result)
    {
        Result = result;
    }
}
