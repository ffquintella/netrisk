using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using ReactiveUI;

namespace GUIClient.Validation;

/// <summary>
/// Holds the validation rules declared by a view-model and aggregates them into
/// (a) a single <see cref="IsValid"/> gate for the Save button and (b) the human-readable
/// text explaining what is still missing.
///
/// Both halves matter: IX-4 requires that a rule gate <c>SaveEnabled</c> <em>and</em> surface
/// its message, so a greyed-out Save always has a stated reason.
/// </summary>
public sealed class ValidationContext : ReactiveObject, IDisposable
{
    private sealed class Rule : IDisposable
    {
        private readonly Action _onChanged;
        private readonly IDisposable _subscription;

        internal Rule(string propertyName, string message, IObservable<bool> isValid, Action onChanged)
        {
            PropertyName = propertyName;
            Message = message;
            _onChanged = onChanged;

            // Fails closed: a rule that has not produced a value yet counts as invalid, so an
            // unevaluated rule can never silently wave a save through.
            _subscription = isValid.Subscribe(
                valid =>
                {
                    if (valid == IsValid) return;
                    IsValid = valid;
                    _onChanged();
                },
                _ =>
                {
                    IsValid = false;
                    _onChanged();
                });
        }

        internal string PropertyName { get; }
        internal string Message { get; }
        internal bool IsValid { get; private set; }

        public void Dispose() => _subscription.Dispose();
    }

    private readonly List<Rule> _rules = new();
    private readonly BehaviorSubject<bool> _isValid = new(true);
    private bool _disposed;

    /// <summary>Emits the current aggregate validity, and again on every change.</summary>
    public IObservable<bool> ValidObservable => _isValid;

    /// <summary><c>true</c> when every declared rule currently passes.</summary>
    public bool IsValid => _isValid.Value;

    /// <summary>
    /// The messages of the rules that currently fail, one per line — suitable for the
    /// tooltip on a disabled Save button. Empty when everything passes.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>
    /// The message of the first failing rule declared for <paramref name="propertyName"/>,
    /// or an empty string when that property is valid. Used for the inline error under a field.
    /// </summary>
    public string MessageFor(string propertyName)
    {
        var failing = _rules.FirstOrDefault(r => !r.IsValid && r.PropertyName == propertyName);
        return failing?.Message ?? string.Empty;
    }

    internal IDisposable AddRule(string propertyName, string message, IObservable<bool> isValid)
    {
        var rule = new Rule(propertyName, message, isValid, Recompute);
        _rules.Add(rule);
        Recompute();

        return Disposable.Create(() =>
        {
            _rules.Remove(rule);
            rule.Dispose();
            Recompute();
        });
    }

    private void Recompute()
    {
        if (_disposed) return;

        var failing = _rules.Where(r => !r.IsValid).ToList();

        var text = string.Join(Environment.NewLine,
            failing.Select(r => r.Message).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct());

        if (text != Text)
        {
            Text = text;
            this.RaisePropertyChanged(nameof(Text));
        }

        var valid = failing.Count == 0;
        if (valid != _isValid.Value)
        {
            _isValid.OnNext(valid);
            this.RaisePropertyChanged(nameof(IsValid));
        }

        // Per-property messages are derived, so any rule change can change any of them.
        this.RaisePropertyChanged(nameof(HasErrors));
    }

    /// <summary>Convenience inverse of <see cref="IsValid"/> for <c>IsVisible</c> bindings.</summary>
    public bool HasErrors => !IsValid;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var rule in _rules) rule.Dispose();
        _rules.Clear();
        _isValid.Dispose();
    }
}
