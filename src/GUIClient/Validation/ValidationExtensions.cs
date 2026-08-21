using System;
using System.Linq.Expressions;
using System.Reactive.Linq;
using ReactiveUI;

namespace GUIClient.Validation;

/// <summary>
/// The declaration surface for view-model validation. Deliberately mirrors the
/// <c>ReactiveUI.Validation</c> API these call sites were originally written against, so
/// the rules read the same, but the implementation is in-tree (see <see cref="ValidationContext"/>)
/// rather than a package that has not been rebuilt for ReactiveUI 24.
/// </summary>
public static class ValidationExtensions
{
    /// <summary>
    /// Declares that <paramref name="property"/> is valid only while
    /// <paramref name="isValid"/> holds, reporting <paramref name="message"/> when it does not.
    /// </summary>
    public static IDisposable ValidationRule<TViewModel, TValue>(
        this TViewModel viewModel,
        Expression<Func<TViewModel, TValue>> property,
        Func<TValue, bool> isValid,
        string message)
        where TViewModel : class, IReactiveObject, IValidatableViewModel
    {
        var stream = viewModel
            .WhenAnyValue(property)
            .Select(value =>
            {
                try
                {
                    return isValid(value);
                }
                catch
                {
                    // A predicate that throws on a half-populated view-model means "not valid yet",
                    // not "tear down the rule".
                    return false;
                }
            });

        return viewModel.ValidationContext.AddRule(GetPropertyName(property), message, stream);
    }

    /// <summary>
    /// Declares a rule for <paramref name="property"/> driven by a caller-supplied observable —
    /// for rules that depend on more than the property's own value (uniqueness, cross-field checks).
    /// </summary>
    public static IDisposable ValidationRule<TViewModel, TValue>(
        this TViewModel viewModel,
        Expression<Func<TViewModel, TValue>> property,
        IObservable<bool> isValid,
        string message)
        where TViewModel : class, IReactiveObject, IValidatableViewModel
    {
        return viewModel.ValidationContext.AddRule(
            GetPropertyName(property),
            message,
            isValid.Catch(Observable.Return(false)));
    }

    /// <summary>Emits the aggregate validity of every rule declared on this view-model.</summary>
    public static IObservable<bool> IsValid<TViewModel>(this TViewModel viewModel)
        where TViewModel : IValidatableViewModel
    {
        return viewModel.ValidationContext.ValidObservable;
    }

    private static string GetPropertyName<TViewModel, TValue>(Expression<Func<TViewModel, TValue>> property)
    {
        var body = property.Body;

        // Unwrap the conversion the compiler inserts for value types / nullable targets.
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary) body = unary.Operand;

        return body is MemberExpression member ? member.Member.Name : string.Empty;
    }
}
