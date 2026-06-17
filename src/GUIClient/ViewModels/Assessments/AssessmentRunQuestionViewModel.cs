using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using DAL.Entities;
using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

/// <summary>
/// Row view-model for a single question inside the paged assessment-run viewer.
/// Wraps the question, the list of predefined answers a respondent may pick, the
/// currently selected answer and the rich-text explanation. Selecting an answer
/// raises <see cref="AnswerChanged"/> so the parent viewer can debounce-save the draft.
/// </summary>
public class AssessmentRunQuestionViewModel : ReactiveObject
{
    public AssessmentQuestion Question { get; }

    public int QuestionId => Question.Id;

    public string QuestionText => Question.Question;

    public string? ExplanationMarkdown => Question.ExplanationMarkdown;

    public bool HasExplanation => !string.IsNullOrWhiteSpace(Question.ExplanationMarkdown);

    public bool IsNested => Question.ParentQuestionId is not null;

    public ObservableCollection<AssessmentAnswer> AvailableAnswers { get; }

    /// <summary>Raised when the selected answer changes; the argument is the answer content JSON.</summary>
    public event Action<AssessmentRunQuestionViewModel, string>? AnswerChanged;

    private bool _suppressNotify;

    private AssessmentAnswer? _selectedAnswer;
    public AssessmentAnswer? SelectedAnswer
    {
        get => _selectedAnswer;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAnswer, value);
            this.RaisePropertyChanged(nameof(IsAnswered));
            if (_suppressNotify) return;
            // The server stores the answer text JSON-encoded; it trims the surrounding
            // quotes when evaluating conditional logic, so a plain JSON string is enough.
            var content = JsonSerializer.Serialize(value?.Answer ?? string.Empty);
            AnswerChanged?.Invoke(this, content);
        }
    }

    public bool IsAnswered => _selectedAnswer != null;

    public AssessmentRunQuestionViewModel(AssessmentQuestion question, ObservableCollection<AssessmentAnswer> availableAnswers)
    {
        Question = question;
        AvailableAnswers = availableAnswers;
    }

    /// <summary>Sets the selected answer without raising the auto-save event (used when loading drafts).</summary>
    public void SetSelectedAnswerSilently(AssessmentAnswer? answer)
    {
        _suppressNotify = true;
        SelectedAnswer = answer;
        _suppressNotify = false;
    }
}
