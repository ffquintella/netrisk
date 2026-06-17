using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

/// <summary>
/// Left-rail entry for the paged assessment-run viewer. Represents either a real
/// question page (<see cref="IsReview"/> = false) or the final review page.
/// </summary>
public class AssessmentRunPageViewModel : ReactiveObject
{
    /// <summary>The page (group) number. Ignored when <see cref="IsReview"/> is true.</summary>
    public int PageNumber { get; init; }

    public bool IsReview { get; init; }

    public string Title { get; init; } = string.Empty;

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set => this.RaiseAndSetIfChanged(ref _isCurrent, value);
    }

    private bool _isComplete;
    public bool IsComplete
    {
        get => _isComplete;
        set => this.RaiseAndSetIfChanged(ref _isComplete, value);
    }
}
