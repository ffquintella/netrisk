using System;
using System.Linq.Expressions;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace GUIClient.Validation;

public static class ValidationExtensions
{
    public static IDisposable ValidationRule<TViewModel, TValue>(
        this TViewModel viewModel,
        Expression<Func<TViewModel, TValue>> property,
        Func<TValue, bool> isValid,
        string message)
    {
        return Disposable.Empty;
    }

    public static IDisposable ValidationRule<TViewModel, TValue>(
        this TViewModel viewModel,
        Expression<Func<TViewModel, TValue>> property,
        IObservable<bool> isValid,
        string message)
    {
        return Disposable.Empty;
    }

    public static IObservable<bool> IsValid<TViewModel>(this TViewModel viewModel)
    {
        return Observable.Return(true);
    }
}
