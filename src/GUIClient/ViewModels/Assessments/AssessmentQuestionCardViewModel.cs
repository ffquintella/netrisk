using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using DAL.Entities;
using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

/// <summary>
/// One question card in the inline assessment builder. Shows a compact summary when
/// collapsed and an in-place editor (text, page, order, rich-text explanation, answer
/// options and a structured show/hide rule) when expanded — no modal dialog.
/// </summary>
public class AssessmentQuestionCardViewModel : ReactiveObject
{
    private readonly AssessmentBuilderViewModel _parent;

    public int Id { get; private set; }
    public int AssessmentId { get; }

    public AssessmentQuestionCardViewModel(AssessmentBuilderViewModel parent, int assessmentId, AssessmentQuestion? question, IEnumerable<AssessmentAnswer>? answers)
    {
        _parent = parent;
        AssessmentId = assessmentId;

        EditCommand = ReactiveCommand.Create(() => _parent.BeginEdit(this));
        SaveCommand = ReactiveCommand.CreateFromTask(() => _parent.SaveCardAsync(this));
        CancelCommand = ReactiveCommand.Create(() => _parent.CancelEdit(this));
        DeleteCommand = ReactiveCommand.CreateFromTask(() => _parent.DeleteCardAsync(this));
        MoveUpCommand = ReactiveCommand.Create(() => _parent.MoveUp(this));
        MoveDownCommand = ReactiveCommand.Create(() => _parent.MoveDown(this));
        AddOptionCommand = ReactiveCommand.Create(AddOption);
        RemoveOptionCommand = ReactiveCommand.Create<AssessmentAnswerEditViewModel>(RemoveOption);

        if (question is not null)
        {
            Id = question.Id;
            _questionText = question.Question;
            _page = question.PageNumber;
            _order = question.Order;
            _explanation = question.ExplanationMarkdown ?? string.Empty;
            LoadCondition(question.ConditionJson);
        }

        if (answers is not null)
            foreach (var a in answers.OrderBy(a => a.Order))
                Options.Add(new AssessmentAnswerEditViewModel(a));
    }

    #region COMMANDS

    public ReactiveCommand<Unit, Unit> EditCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveUpCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveDownCommand { get; }
    public ReactiveCommand<Unit, Unit> AddOptionCommand { get; }
    public ReactiveCommand<AssessmentAnswerEditViewModel, Unit> RemoveOptionCommand { get; }

    #endregion

    #region EDITABLE STATE

    private string _questionText = string.Empty;
    public string QuestionText
    {
        get => _questionText;
        set
        {
            this.RaiseAndSetIfChanged(ref _questionText, value);
            this.RaisePropertyChanged(nameof(DisplayText));
        }
    }

    private int _page = 1;
    public int Page
    {
        get => _page;
        set
        {
            this.RaiseAndSetIfChanged(ref _page, value < 1 ? 1 : value);
            this.RaisePropertyChanged(nameof(PageBadge));
        }
    }

    private int _order;
    public int Order
    {
        get => _order;
        set => this.RaiseAndSetIfChanged(ref _order, value < 0 ? 0 : value);
    }

    private string _explanation = string.Empty;
    public string Explanation
    {
        get => _explanation;
        set
        {
            this.RaiseAndSetIfChanged(ref _explanation, value);
            this.RaisePropertyChanged(nameof(HasExplanation));
        }
    }

    public ObservableCollection<AssessmentAnswerEditViewModel> Options { get; } = new();

    /// <summary>Existing options removed during editing, deleted from the server on save.</summary>
    public List<AssessmentAnswerEditViewModel> RemovedOptions { get; } = new();

    #endregion

    #region CONDITION (show/hide rule)

    private bool _conditionEnabled;
    public bool ConditionEnabled
    {
        get => _conditionEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _conditionEnabled, value);
            this.RaisePropertyChanged(nameof(HasCondition));
        }
    }

    private QuestionRefItem? _conditionSource;
    public QuestionRefItem? ConditionSource
    {
        get => _conditionSource;
        set => this.RaiseAndSetIfChanged(ref _conditionSource, value);
    }

    public ObservableCollection<ConditionOperatorOption> Operators { get; } = new()
    {
        new ConditionOperatorOption("equals", "equals"),
        new ConditionOperatorOption("in", "is one of (comma separated)"),
        new ConditionOperatorOption("notempty", "is answered")
    };

    private ConditionOperatorOption _conditionOperator;
    public ConditionOperatorOption ConditionOperator
    {
        get => _conditionOperator;
        set => this.RaiseAndSetIfChanged(ref _conditionOperator, value);
    }

    private string _conditionValue = string.Empty;
    public string ConditionValue
    {
        get => _conditionValue;
        set => this.RaiseAndSetIfChanged(ref _conditionValue, value);
    }

    /// <summary>The other questions a show/hide rule can reference (populated by the parent on edit).</summary>
    public ObservableCollection<QuestionRefItem> ConditionSourceQuestions { get; } = new();

    private int _pendingConditionSourceId;

    private void LoadCondition(string? conditionJson)
    {
        _conditionOperator = Operators[0];
        if (string.IsNullOrWhiteSpace(conditionJson)) return;
        try
        {
            var cond = JsonSerializer.Deserialize<StoredCondition>(conditionJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (cond is null || cond.QuestionId <= 0) return;

            _conditionEnabled = true;
            _pendingConditionSourceId = cond.QuestionId;
            _conditionOperator = Operators.FirstOrDefault(o =>
                string.Equals(o.Token, cond.Operator, StringComparison.OrdinalIgnoreCase)) ?? Operators[0];
            _conditionValue = cond.Value ?? string.Empty;
        }
        catch
        {
            // Leave the rule disabled if the stored condition is unreadable.
        }
    }

    /// <summary>Rebuilds the reference-question list (other questions) and restores any saved selection.</summary>
    public void RefreshConditionSources(IEnumerable<AssessmentQuestionCardViewModel> allCards)
    {
        ConditionSourceQuestions.Clear();
        foreach (var c in allCards.Where(c => !ReferenceEquals(c, this) && c.Id > 0))
            ConditionSourceQuestions.Add(new QuestionRefItem(c.Id, c.RefLabel));

        var targetId = ConditionSource?.Id ?? _pendingConditionSourceId;
        ConditionSource = ConditionSourceQuestions.FirstOrDefault(q => q.Id == targetId);
    }

    /// <summary>Serializes the rule to the ConditionJson the server expects, or null when disabled.</summary>
    public string? BuildConditionJson()
    {
        if (!ConditionEnabled || ConditionSource is null) return null;
        var op = ConditionOperator?.Token ?? "equals";
        var value = op == "notempty" ? null : ConditionValue;
        return JsonSerializer.Serialize(new StoredCondition
        {
            QuestionId = ConditionSource.Id,
            Operator = op,
            Value = value
        });
    }

    public bool HasCondition => ConditionEnabled && ConditionSource is not null;

    #endregion

    #region DISPLAY (collapsed summary)

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set => this.RaiseAndSetIfChanged(ref _isEditing, value);
    }

    public string DisplayText => string.IsNullOrWhiteSpace(QuestionText) ? "(untitled question)" : QuestionText;
    public string RefLabel => $"P{Page} · {(QuestionText.Length > 50 ? QuestionText[..50] + "…" : QuestionText)}";
    public string PageBadge => $"P{Page}";
    public bool HasExplanation => !string.IsNullOrWhiteSpace(Explanation);

    public void SetId(int id) => Id = id;

    #endregion

    private void AddOption() => Options.Add(new AssessmentAnswerEditViewModel());

    private void RemoveOption(AssessmentAnswerEditViewModel option)
    {
        if (option.Id > 0) RemovedOptions.Add(option);
        Options.Remove(option);
    }

    private class StoredCondition
    {
        public int QuestionId { get; set; }
        public string Operator { get; set; } = "equals";
        public string? Value { get; set; }
    }
}

public record QuestionRefItem(int Id, string Label);

public record ConditionOperatorOption(string Token, string Label);
