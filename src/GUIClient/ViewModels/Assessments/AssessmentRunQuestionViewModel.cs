using System;
using System.Collections.ObjectModel;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Assessments;
using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

/// <summary>
/// Row view-model for a single question inside the paged assessment-run viewer.
/// Wraps the question, the list of predefined answers a respondent may pick, the
/// currently selected answer and the rich-text explanation. Selecting an answer
/// raises <see cref="AnswerChanged"/> so the parent viewer can debounce-save the draft.
///
/// Since GitHub #80 (S44) it also carries the assessor's comment on the answer, raising
/// <see cref="CommentChanged"/> for the same debounced save, and the answer's evidence files.
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

    /// <summary>Raised when the assessor edits the comment; the argument is the comment as typed.</summary>
    public event Action<AssessmentRunQuestionViewModel, string?>? CommentChanged;

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

    private bool _suppressCommentNotify;

    private string? _comment;
    /// <summary>The assessor's comment on this answer.</summary>
    public string? Comment
    {
        get => _comment;
        set
        {
            if (string.Equals(_comment, value, StringComparison.Ordinal)) return;
            this.RaiseAndSetIfChanged(ref _comment, value);
            if (_suppressCommentNotify) return;
            CommentChanged?.Invoke(this, value);
        }
    }

    /// <summary>The comment box's length cap, the same bound the server enforces.</summary>
    public int CommentMaxLength => AssessmentEvidencePolicy.MaxCommentLength;

    private bool _canEditComment = true;
    public bool CanEditComment
    {
        get => _canEditComment;
        set => this.RaiseAndSetIfChanged(ref _canEditComment, value);
    }

    /// <summary>The evidence files attached to this answer.</summary>
    public ObservableCollection<AssessmentEvidenceItemViewModel> Evidence { get; } = new();

    private bool _canAttachEvidence;
    /// <summary>Whether the attach button is offered: a real, open run.</summary>
    public bool CanAttachEvidence
    {
        get => _canAttachEvidence;
        private set => this.RaiseAndSetIfChanged(ref _canAttachEvidence, value);
    }

    /// <summary>Opens the file picker and attaches the chosen file; a no-op until the viewer configures it.</summary>
    public ReactiveCommand<RxVoid, RxVoid> AttachEvidenceCommand { get; private set; }

    public AssessmentRunQuestionViewModel(AssessmentQuestion question, ObservableCollection<AssessmentAnswer> availableAnswers)
    {
        Question = question;
        AvailableAnswers = availableAnswers;
        AttachEvidenceCommand = ReactiveCommand.Create(() => { });
    }

    /// <summary>Sets the selected answer without raising the auto-save event (used when loading drafts).</summary>
    public void SetSelectedAnswerSilently(AssessmentAnswer? answer)
    {
        _suppressNotify = true;
        SelectedAnswer = answer;
        _suppressNotify = false;
    }

    /// <summary>Sets the comment without raising the auto-save event (used when loading drafts).</summary>
    public void SetCommentSilently(string? comment)
    {
        _suppressCommentNotify = true;
        Comment = comment;
        _suppressCommentNotify = false;
    }

    /// <summary>
    /// Wires the attach command to the viewer, which owns the file picker and the services, and says
    /// whether attaching is offered at all.
    /// </summary>
    public void ConfigureEvidence(bool canAttach, Func<AssessmentRunQuestionViewModel, Task> attach)
    {
        CanAttachEvidence = canAttach;
        AttachEvidenceCommand = ReactiveCommand.CreateFromTask(() => attach(this));
        this.RaisePropertyChanged(nameof(AttachEvidenceCommand));
    }
}
