using System;
using System.Text;
using DAL.Entities;
using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

/// <summary>
/// Editable row for a single answer option inside the assessment builder's question card.
/// Wraps the byte[] risk subject as a string and keeps the original id so the builder can
/// tell new options (Id == 0) from existing ones when saving.
/// </summary>
public class AssessmentAnswerEditViewModel : ReactiveObject
{
    public int Id { get; }

    private string _answer = string.Empty;
    public string Answer
    {
        get => _answer;
        set => this.RaiseAndSetIfChanged(ref _answer, value);
    }

    private float _risk;
    public float Risk
    {
        get => _risk;
        set => this.RaiseAndSetIfChanged(ref _risk, value < 0 ? 0 : value > 10 ? 10 : value);
    }

    private string _subject = string.Empty;
    public string Subject
    {
        get => _subject;
        set => this.RaiseAndSetIfChanged(ref _subject, value);
    }

    public AssessmentAnswerEditViewModel()
    {
    }

    public AssessmentAnswerEditViewModel(AssessmentAnswer answer)
    {
        Id = answer.Id;
        _answer = answer.Answer;
        _risk = answer.RiskScore;
        _subject = answer.RiskSubject is { Length: > 0 } ? Encoding.UTF8.GetString(answer.RiskSubject) : string.Empty;
    }

    public AssessmentAnswer ToEntity(int assessmentId, int questionId) => new()
    {
        Id = Id,
        AssessmentId = assessmentId,
        QuestionId = questionId,
        Answer = Answer,
        RiskScore = Risk,
        RiskSubject = Encoding.UTF8.GetBytes(Subject ?? string.Empty)
    };
}
