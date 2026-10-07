using System;
using System.Linq.Expressions;
using System.Reactive.Linq;
using ReactiveUI;

namespace GUIClient.Validation;

/// <summary>
/// The declaration surface for view-model validation. Deliberately mirrors the
/// <c>ReactiveUI.Validation</c> API these call sites were originally written against, so
/// the rules read the same, but the implementation is in-tree (see <see cref="ValidationContext"/>)
/// rather than a package that has not been rebuilt for ReactiveUI 24 or later.
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
        // Must stay WhenAnyValueUnsafe. Since ReactiveUI 25, WhenAnyValue is ReactiveUI.Binding's: a source
        // generator writes one dispatch per call site at build time, and it cannot write one here, where the
        // view-model is a type parameter and the property a runtime argument (RXUIBIND016 / RXUIBIND021). The
        // plain call compiles and then throws InvalidOperationException, so every rule — and every Save button
        // gated on one — would fail. The Unsafe twin walks the expression by reflection, which is what
        // ReactiveUI 24's WhenAnyValue did for every call, so the rules observe exactly what they did before.
        // It is marked [RequiresUnreferencedCode]; the shipped client is published untrimmed (build/Build.cs,
        // build/Build.Installers.cs), so the reflection has every member it needs.
        var stream = viewModel
            .WhenAnyValueUnsafe(property)
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
