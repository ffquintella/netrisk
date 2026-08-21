using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using GUIClient.Validation;
using ReactiveUI;
using ReactiveUI.Builder;
using Xunit;

namespace GUIClient.Tests.Validation;

/// <summary>
/// Covers the in-tree validation layer introduced by Track 1 Milestone 1.5 Phase A.
///
/// This matters more than the size of the file suggests: the GUI's previous validation
/// infrastructure was a no-op stub (every rule returned <c>Disposable.Empty</c> and
/// <c>IsValid()</c> returned <c>Observable.Return(true)</c>), so no declared rule in any
/// view-model gated anything for six months. Every Save button in the desktop client now depends
/// on the behaviour asserted here.
/// </summary>
public class ValidationContextTests
{
    /// <summary>
    /// ReactiveUI 24 requires explicit initialization; <c>WhenAnyValue</c> — which the rules are
    /// built on — throws without it. The real app initializes via <c>UseReactiveUI</c>.
    /// </summary>
    static ValidationContextTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
    }

    private sealed class TestViewModel : ReactiveObject, IValidatableViewModel
    {
        public ValidationContext ValidationContext { get; } = new();

        private string _name = "";
        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        private int _quantity;
        public int Quantity
        {
            get => _quantity;
            set => this.RaiseAndSetIfChanged(ref _quantity, value);
        }

        private string? _choice;
        public string? Choice
        {
            get => _choice;
            set => this.RaiseAndSetIfChanged(ref _choice, value);
        }
    }

    private static TestViewModel WithThreeRules()
    {
        var vm = new TestViewModel();

        vm.ValidationRule(x => x.Name, n => !string.IsNullOrWhiteSpace(n), "Name is required.");
        vm.ValidationRule(x => x.Quantity, q => q > 0, "Quantity must be positive.");
        vm.ValidationRule(x => x.Choice, c => c != null, "Pick one.");

        return vm;
    }

    [Fact]
    public void NoRulesDeclared_IsValid()
    {
        var vm = new TestViewModel();

        Assert.True(vm.ValidationContext.IsValid);
        Assert.False(vm.ValidationContext.HasErrors);
        Assert.Equal("", vm.ValidationContext.Text);
    }

    [Fact]
    public void FailingRules_MakeTheContextInvalidImmediately()
    {
        var vm = WithThreeRules();

        // Rules are evaluated as they are declared, so a view-model is never briefly "valid"
        // just because nothing has changed yet.
        Assert.False(vm.ValidationContext.IsValid);
        Assert.True(vm.ValidationContext.HasErrors);
    }

    [Fact]
    public void SubscribersReceiveTheCurrentValueImmediately()
    {
        var vm = WithThreeRules();
        var observed = new List<bool>();

        vm.IsValid().Subscribe(observed.Add);

        Assert.Equal(new[] { false }, observed);
    }

    [Fact]
    public void SummaryListsEveryFailingRule_AndShrinksAsTheyPass()
    {
        var vm = WithThreeRules();

        Assert.Equal(3, vm.ValidationContext.Text.Split(Environment.NewLine).Length);

        vm.Name = "abc";

        Assert.Equal(2, vm.ValidationContext.Text.Split(Environment.NewLine).Length);
        Assert.DoesNotContain("Name is required.", vm.ValidationContext.Text);
    }

    [Fact]
    public void MessageForNamesTheFailingProperty()
    {
        var vm = WithThreeRules();

        Assert.Equal("Name is required.", vm.ValidationContext.MessageFor("Name"));
        Assert.Equal("", vm.ValidationContext.MessageFor("NotAProperty"));

        vm.Name = "abc";

        Assert.Equal("", vm.ValidationContext.MessageFor("Name"));
    }

    [Fact]
    public void BecomesValidOnlyWhenEveryRulePasses()
    {
        var vm = WithThreeRules();

        vm.Name = "abc";
        Assert.False(vm.ValidationContext.IsValid);

        vm.Quantity = 5;
        Assert.False(vm.ValidationContext.IsValid);

        vm.Choice = "x";
        Assert.True(vm.ValidationContext.IsValid);
        Assert.Equal("", vm.ValidationContext.Text);
    }

    [Fact]
    public void OnlyDistinctChangesAreEmitted()
    {
        var vm = WithThreeRules();
        var observed = new List<bool>();
        vm.IsValid().Subscribe(observed.Add);

        vm.Name = "abc";
        vm.Quantity = 5;
        vm.Choice = "x";

        // One emission for the initial false, one for the flip to true — the intermediate
        // still-invalid states must not re-notify.
        Assert.Equal(new[] { false, true }, observed);
    }

    [Fact]
    public void RegressingARule_GoesInvalidAgain()
    {
        var vm = WithThreeRules();
        vm.Name = "abc";
        vm.Quantity = 5;
        vm.Choice = "x";

        var observed = new List<bool>();
        vm.IsValid().Subscribe(observed.Add);

        vm.Name = "";

        Assert.Equal(new[] { true, false }, observed);
        Assert.False(vm.ValidationContext.IsValid);
    }

    [Fact]
    public void ObservableOverload_FollowsTheSuppliedObservable()
    {
        var vm = new TestViewModel();
        var gate = new BehaviorSubject<bool>(false);

        vm.ValidationRule(x => x.Name, gate, "Gated.");

        Assert.False(vm.ValidationContext.IsValid);

        gate.OnNext(true);

        Assert.True(vm.ValidationContext.IsValid);
    }

    [Fact]
    public void ThrowingPredicate_CountsAsInvalidRatherThanTearingDownTheRule()
    {
        var vm = new TestViewModel();

        // A predicate can legitimately throw while a view-model is half-populated during activation.
        vm.ValidationRule(x => x.Name, _ => throw new InvalidOperationException("boom"), "Throwing rule.");

        Assert.False(vm.ValidationContext.IsValid);
    }

    [Fact]
    public void FaultingObservable_CountsAsInvalid()
    {
        var vm = new TestViewModel();
        var gate = new Subject<bool>();

        vm.ValidationRule(x => x.Name, gate, "Faulting rule.");
        gate.OnError(new InvalidOperationException("boom"));

        Assert.False(vm.ValidationContext.IsValid);
    }

    [Fact]
    public void DisposingARule_RemovesItFromTheAggregate()
    {
        var vm = new TestViewModel();

        var handle = vm.ValidationRule(x => x.Name, n => n.Length > 3, "Too short.");
        Assert.False(vm.ValidationContext.IsValid);

        handle.Dispose();

        Assert.True(vm.ValidationContext.IsValid);
        Assert.Equal("", vm.ValidationContext.Text);
    }

    [Fact]
    public void DisposingTheContext_IsIdempotent()
    {
        var vm = WithThreeRules();

        vm.ValidationContext.Dispose();
        vm.ValidationContext.Dispose();
    }
}
