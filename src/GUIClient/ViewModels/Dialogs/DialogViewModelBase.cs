using System;
using System.Linq;
using System.Reflection;
using ReactiveUI;
using System.Windows.Input;
using GUIClient.Extensions;
using Splat;

namespace GUIClient.ViewModels.Dialogs;

public class DialogViewModelBase<TResult> : ViewModelBase
    where TResult : DialogResultBase
{
    public event EventHandler<DialogResultEventArgs<TResult>>? CloseRequested;

    public ICommand CloseCommand { get; }

    protected DialogViewModelBase()
    {
        CloseCommand = ReactiveCommand.Create(Close);
    }

    protected void Close() => Close(default);

    protected void Close(TResult? result)
    {
        var args = new DialogResultEventArgs<TResult>(result);

        CloseRequested.Raise(this, args);
    }
    
    private static DialogViewModelBase<TResult> CreateViewModel<TResult>(string viewModelName)
        where TResult : DialogResultBase
    {
        var viewModelType = GetViewModelType(viewModelName);
        if (viewModelType is null)
        {
            throw new InvalidOperationException($"View model {viewModelName} was not found!");
        }

        return (DialogViewModelBase<TResult>) GetViewModel(viewModelType);
    }

    private static Type GetViewModelType(string viewModelName)
    {
        var viewModelsAssembly = Assembly.GetAssembly(typeof(ViewModelBase));
        if (viewModelsAssembly is null)
        {
            throw new InvalidOperationException("Broken installation!");
        }

        var viewModelTypes = viewModelsAssembly.GetTypes();

        return viewModelTypes.SingleOrDefault(t => t.Name == viewModelName);
    }

    private static object GetViewModel(Type type) => Locator.Current.GetRequiredService(type);
}

public class DialogViewModelBase : DialogViewModelBase<DialogResultBase>
{

}
