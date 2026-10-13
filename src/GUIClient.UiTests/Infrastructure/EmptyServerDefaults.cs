using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NSubstitute.Extensions;

namespace GUIClient.UiTests.Infrastructure;

/// <summary>
/// Makes a fresh double answer like a server that has no data yet, instead of like nothing at all.
///
/// NSubstitute's own default for <c>Task&lt;List&lt;Risk&gt;&gt;</c> is a completed task holding
/// <c>null</c>, which no real REST client ever returns — it returns an empty list. Left alone, a
/// view-model iterating the result would fail on the harness rather than on anything the client does.
/// So every member of the interface whose result is a collection (bare, inside a <c>Task</c>/
/// <c>ValueTask</c>, or inside a value tuple such as the paged <c>(items, total)</c> results) answers
/// with a new empty one. Anything a test configures with <c>.Returns(…)</c> afterwards takes
/// precedence; scalars and single objects keep NSubstitute's defaults (0, false, "", null).
/// </summary>
internal static class EmptyServerDefaults
{
    private static readonly MethodInfo ReturnsForAllFactory = typeof(EmptyServerDefaults)
        .GetMethod(nameof(ReturnsForAll), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static void Apply(object substitute, Type serviceInterface)
    {
        var returnTypes = new[] { serviceInterface }
            .Concat(serviceInterface.GetInterfaces())
            .SelectMany(i => i.GetMethods())
            .Select(m => m.ReturnType)
            .Where(t => t != typeof(void))
            .Distinct();

        foreach (var type in returnTypes)
        {
            if (TryMakeFactory(type, out var factory))
                ReturnsForAllFactory.MakeGenericMethod(type).Invoke(null, [substitute, factory]);
        }
    }

    private static void ReturnsForAll<T>(object substitute, Func<object?> factory) =>
        substitute.ReturnsForAll<T>(_ => (T)factory()!);

    /// <summary>A factory for an "empty" value of <paramref name="type"/>, when one is meaningful.</summary>
    private static bool TryMakeFactory(Type type, out Func<object?> factory)
    {
        factory = null!;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = type.GetGenericArguments()[0];
            if (!TryMakeFactory(inner, out var innerFactory)) return false;

            var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner);
            factory = () => fromResult.Invoke(null, [innerFactory()]);
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var inner = type.GetGenericArguments()[0];
            if (!TryMakeFactory(inner, out var innerFactory)) return false;

            factory = () => Activator.CreateInstance(type, innerFactory());
            return true;
        }

        // (items, total) as a ValueTuple, or as the System.Tuple some older clients still return.
        if (typeof(ITuple).IsAssignableFrom(type) && type.IsGenericType)
        {
            var items = type.GetGenericArguments()
                .Select(t => TryMakeFactory(t, out var f) ? f : () => t.IsValueType ? Activator.CreateInstance(t) : null)
                .ToArray();

            // Only worth overriding when at least one element is a collection.
            if (!type.GetGenericArguments().Any(t => TryMakeFactory(t, out _))) return false;

            factory = () => Activator.CreateInstance(type, items.Select(f => f()).ToArray());
            return true;
        }

        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            factory = () => Array.CreateInstance(element, 0);
            return true;
        }

        if (!type.IsGenericType || type == typeof(string)) return false;

        var definition = type.GetGenericTypeDefinition();
        var args = type.GetGenericArguments();

        Type? concrete = null;
        if (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(IEnumerable<>) ||
            definition == typeof(ICollection<>) || definition == typeof(IReadOnlyList<>) ||
            definition == typeof(IReadOnlyCollection<>))
            concrete = typeof(List<>).MakeGenericType(args);
        else if (definition == typeof(ObservableCollection<>))
            concrete = type;
        else if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) ||
                 definition == typeof(IReadOnlyDictionary<,>))
            concrete = typeof(Dictionary<,>).MakeGenericType(args);
        else if (definition == typeof(HashSet<>) || definition == typeof(ISet<>))
            concrete = typeof(HashSet<>).MakeGenericType(args);

        if (concrete == null || !type.IsAssignableFrom(concrete)) return false;

        factory = () => Activator.CreateInstance(concrete);
        return true;
    }
}
