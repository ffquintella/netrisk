﻿using System.Threading;
using System.Threading.Tasks;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.ViewModels.Dialogs;



public abstract class ParameterizedDialogViewModelBase<TResult, TParameter> : DialogViewModelBase<TResult>
    where TResult : DialogResultBase
    where TParameter : NavigationParameterBase
{
    public abstract void Activate(TParameter parameter);
}

public abstract class ParameterizedDialogViewModelBaseAsync<TResult, TParameter> : DialogViewModelBase<TResult>
    where TResult : DialogResultBase
    where TParameter : NavigationParameterBase
{
    public abstract Task ActivateAsync(TParameter parameter, CancellationToken cancellationToken = default);
}

public abstract class ParameterizedDialogViewModelBase<TParameter> : ParameterizedDialogViewModelBase<DialogResultBase, TParameter>
    where TParameter : NavigationParameterBase
{

}

