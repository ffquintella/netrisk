using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Mapster;
using Model.Assessments;
using Model.DTO;
using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

public class AssessmentRunViewerViewModel : ParameterizedDialogViewModelBaseAsync<AssessmentRunViewerResult,
    AssessmentRunViewerParameter>
{
    #region LANGUAGE

    public string StrTitle => Localizer["AssessmentRunViewer"];
    public string StrPages => Localizer["Pages"];
    public string StrPrevious => Localizer["Previous"];
    public string StrNext => Localizer["Next"];
    public string StrReview => Localizer["Review"];
    public string StrProgress => Localizer["Progress"];
    public string StrExplanation => Localizer["Explanation"];
    public string StrAnswer => Localizer["Answer"];
    public string StrUnansweredItems => Localizer["UnansweredRequiredItemsMSG"];
    public string StrAllAnswered => Localizer["AllRequiredAnsweredMSG"];
    public string StrGoToPage => Localizer["GoToPage"];
    public string StrPage => Localizer["Page"];

    #endregion

    #region SERVICES

    private IAssessmentsService AssessmentsService { get; } = GetService<IAssessmentsService>();

    #endregion

    #region PROPERTIES

    private ObservableCollection<AssessmentRunPageViewModel> _pages = new();
    public ObservableCollection<AssessmentRunPageViewModel> Pages
    {
        get => _pages;
        set => this.RaiseAndSetIfChanged(ref _pages, value);
    }

    private ObservableCollection<AssessmentRunQuestionViewModel> _currentQuestions = new();
    public ObservableCollection<AssessmentRunQuestionViewModel> CurrentQuestions
    {
        get => _currentQuestions;
        set => this.RaiseAndSetIfChanged(ref _currentQuestions, value);
    }

    private ObservableCollection<AssessmentRunReviewItem> _reviewItems = new();
    public ObservableCollection<AssessmentRunReviewItem> ReviewItems
    {
        get => _reviewItems;
        set => this.RaiseAndSetIfChanged(ref _reviewItems, value);
    }

    private string _currentPageTitle = string.Empty;
    public string CurrentPageTitle
    {
        get => _currentPageTitle;
        set => this.RaiseAndSetIfChanged(ref _currentPageTitle, value);
    }

    private bool _isReviewPage;
    public bool IsReviewPage
    {
        get => _isReviewPage;
        set => this.RaiseAndSetIfChanged(ref _isReviewPage, value);
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    private string _progressText = "0%";
    public string ProgressText
    {
        get => _progressText;
        set => this.RaiseAndSetIfChanged(ref _progressText, value);
    }

    private string _savedAtText = string.Empty;
    public string SavedAtText
    {
        get => _savedAtText;
        set => this.RaiseAndSetIfChanged(ref _savedAtText, value);
    }

    private bool _canGoPrevious;
    public bool CanGoPrevious
    {
        get => _canGoPrevious;
        set => this.RaiseAndSetIfChanged(ref _canGoPrevious, value);
    }

    private bool _canGoNext;
    public bool CanGoNext
    {
        get => _canGoNext;
        set => this.RaiseAndSetIfChanged(ref _canGoNext, value);
    }

    private bool _isReadOnly;
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set => this.RaiseAndSetIfChanged(ref _isReadOnly, value);
    }

    #endregion

    #region COMMANDS

    public ReactiveCommand<Unit, Unit> PreviousCommand { get; }
    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseViewerCommand { get; }
    public ReactiveCommand<int, Unit> JumpToPageCommand { get; }
    public ReactiveCommand<AssessmentRunPageViewModel, Unit> GoToPageItemCommand { get; }

    #endregion

    #region FIELDS

    private Assessment? _assessment;
    private AssessmentRun? _run;

    private readonly List<AssessmentQuestion> _allQuestions = new();
    private List<AssessmentQuestion> _requiredQuestions = new();
    private readonly Dictionary<int, ObservableCollection<AssessmentAnswer>> _answersByQuestion = new();
    private readonly Dictionary<int, string> _selectedAnswerText = new();   // questionId -> answer text
    private readonly HashSet<int> _answeredQuestions = new();

    private readonly object _pendingLock = new();
    private readonly Dictionary<int, string> _pendingSaves = new();         // questionId -> answer content json
    private readonly Subject<Unit> _saveSignal = new();
    private IDisposable? _saveSubscription;

    private int _currentIndex;
    private bool _runChanged;

    #endregion

    #region CONSTRUCTOR

    public AssessmentRunViewerViewModel()
    {
        PreviousCommand = ReactiveCommand.CreateFromTask(GoPreviousAsync);
        NextCommand = ReactiveCommand.CreateFromTask(GoNextAsync);
        CloseViewerCommand = ReactiveCommand.CreateFromTask(CloseViewerAsync);
        JumpToPageCommand = ReactiveCommand.CreateFromTask<int>(JumpToPageAsync);
        GoToPageItemCommand = ReactiveCommand.CreateFromTask<AssessmentRunPageViewModel>(GoToPageItemAsync);

        // Debounced draft auto-save: ~2s after the last change, flush every pending answer.
        _saveSubscription = _saveSignal
            .Throttle(TimeSpan.FromSeconds(2))
            .Subscribe(_ =>
            {
                FlushPendingAsync().ConfigureAwait(false);
            });
    }

    #endregion

    #region ACTIVATION

    public override async Task ActivateAsync(AssessmentRunViewerParameter parameter, CancellationToken cancellationToken = default)
    {
        _assessment = parameter.Assessment;
        _run = parameter.AssessmentRun;

        if (_assessment is null || _run is null)
        {
            Logger.Error("Assessment and run are required to open the run viewer");
            Close();
            return;
        }

        IsReadOnly = _run.Status == (int)AssessmentStatus.Submitted;

        // Load the question catalogue and predefined answers.
        var questions = AssessmentsService.GetAssessmentQuestions(_assessment.Id) ?? new List<AssessmentQuestion>();
        _allQuestions.Clear();
        _allQuestions.AddRange(questions);
        _requiredQuestions = _allQuestions.Where(q => string.IsNullOrEmpty(q.ConditionJson)).ToList();

        var answers = AssessmentsService.GetAssessmentAnswers(_assessment.Id) ?? new List<AssessmentAnswer>();
        _answersByQuestion.Clear();
        foreach (var group in answers.GroupBy(a => a.QuestionId))
            _answersByQuestion[group.Key] = new ObservableCollection<AssessmentAnswer>(group.OrderBy(a => a.Order));

        // Resume any previously saved draft answers.
        var drafts = await AssessmentsService.GetDraftAnswersAsync(_run.Id) ?? new List<AssessmentRunAnswer>();
        foreach (var draft in drafts)
        {
            var text = DecodeAnswerText(draft.AnswerContentJson);
            if (string.IsNullOrEmpty(text)) continue;
            _selectedAnswerText[draft.AssessmentQuestionId] = text;
            _answeredQuestions.Add(draft.AssessmentQuestionId);
        }

        BuildPages();
        RecalculateProgress();

        // Resume at the last visited page if it still exists, otherwise the first.
        var resumePage = _run.CurrentPageIndex;
        var startIndex = Pages.ToList().FindIndex(p => !p.IsReview && p.PageNumber == resumePage);
        if (startIndex < 0) startIndex = 0;

        await LoadPageAtAsync(startIndex);
    }

    private void BuildPages()
    {
        var pageNumbers = _allQuestions.Select(q => q.PageNumber).Distinct().OrderBy(n => n).ToList();
        if (pageNumbers.Count == 0) pageNumbers.Add(1);

        var pages = new ObservableCollection<AssessmentRunPageViewModel>();
        foreach (var pn in pageNumbers)
        {
            pages.Add(new AssessmentRunPageViewModel
            {
                PageNumber = pn,
                Title = $"{Localizer["Page"]} {pn}",
                IsComplete = IsPageComplete(pn)
            });
        }

        pages.Add(new AssessmentRunPageViewModel { IsReview = true, Title = Localizer["Review"] });
        Pages = pages;
    }

    #endregion

    #region NAVIGATION

    private async Task LoadPageAtAsync(int index)
    {
        if (index < 0 || index >= Pages.Count) return;

        // Persist anything pending before switching pages so conditional logic sees fresh answers.
        await FlushPendingAsync();

        _currentIndex = index;
        var page = Pages[index];

        foreach (var p in Pages)
            p.IsCurrent = ReferenceEquals(p, page);

        CanGoPrevious = index > 0;
        CanGoNext = index < Pages.Count - 1;

        if (page.IsReview)
        {
            IsReviewPage = true;
            CurrentPageTitle = Localizer["Review"];
            CurrentQuestions = new ObservableCollection<AssessmentRunQuestionViewModel>();
            BuildReviewItems();
        }
        else
        {
            IsReviewPage = false;
            CurrentPageTitle = page.Title;
            if (_run is not null) _run.CurrentPageIndex = page.PageNumber;
            await LoadQuestionsForPageAsync(page.PageNumber);
        }
    }

    private async Task LoadQuestionsForPageAsync(int pageNumber)
    {
        var visible = await AssessmentsService.GetVisibleQuestionsForPageAsync(_run!.Id, pageNumber)
                      ?? _allQuestions.Where(q => q.PageNumber == pageNumber).OrderBy(q => q.Order).ToList();

        var items = new ObservableCollection<AssessmentRunQuestionViewModel>();
        foreach (var question in visible)
        {
            if (!_answersByQuestion.TryGetValue(question.Id, out var availableAnswers))
                availableAnswers = new ObservableCollection<AssessmentAnswer>();

            var item = new AssessmentRunQuestionViewModel(question, availableAnswers);

            // Restore the saved selection (silently, so it does not trigger a redundant save).
            if (_selectedAnswerText.TryGetValue(question.Id, out var savedText))
            {
                var match = availableAnswers.FirstOrDefault(a =>
                    string.Equals(a.Answer, savedText, StringComparison.OrdinalIgnoreCase));
                if (match != null) item.SetSelectedAnswerSilently(match);
            }

            item.AnswerChanged += OnAnswerChanged;
            items.Add(item);
        }

        CurrentQuestions = items;
    }

    private async Task GoPreviousAsync()
    {
        if (_currentIndex > 0) await LoadPageAtAsync(_currentIndex - 1);
    }

    private async Task GoNextAsync()
    {
        if (_currentIndex < Pages.Count - 1) await LoadPageAtAsync(_currentIndex + 1);
    }

    private async Task JumpToPageAsync(int pageNumber)
    {
        var index = Pages.ToList().FindIndex(p => !p.IsReview && p.PageNumber == pageNumber);
        if (index >= 0) await LoadPageAtAsync(index);
    }

    private async Task GoToPageItemAsync(AssessmentRunPageViewModel? page)
    {
        if (page is null) return;
        var index = Pages.IndexOf(page);
        if (index >= 0) await LoadPageAtAsync(index);
    }

    private async Task CloseViewerAsync()
    {
        await FlushPendingAsync();
        await PersistRunStateAsync();

        Close(new AssessmentRunViewerResult { Action = ResultActions.Ok, RunChanged = _runChanged });
    }

    #endregion

    #region AUTO-SAVE & PROGRESS

    private void OnAnswerChanged(AssessmentRunQuestionViewModel item, string contentJson)
    {
        if (IsReadOnly) return;

        var text = DecodeAnswerText(contentJson);
        if (string.IsNullOrEmpty(text))
        {
            _selectedAnswerText.Remove(item.QuestionId);
            _answeredQuestions.Remove(item.QuestionId);
        }
        else
        {
            _selectedAnswerText[item.QuestionId] = text;
            _answeredQuestions.Add(item.QuestionId);
        }

        lock (_pendingLock)
        {
            _pendingSaves[item.QuestionId] = contentJson;
        }

        RecalculateProgress();
        _saveSignal.OnNext(Unit.Default);
    }

    private async Task FlushPendingAsync()
    {
        Dictionary<int, string> toSave;
        lock (_pendingLock)
        {
            if (_pendingSaves.Count == 0) return;
            toSave = new Dictionary<int, string>(_pendingSaves);
            _pendingSaves.Clear();
        }

        var anySaved = false;
        foreach (var (questionId, content) in toSave)
        {
            var result = await AssessmentsService.SaveDraftAnswerAsync(_run!.Id, questionId, content);
            if (result != null) anySaved = true;
        }

        if (!anySaved) return;
        _runChanged = true;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            SavedAtText = $"{Localizer["SavedAt"]} {DateTime.Now:HH:mm}";
            foreach (var page in Pages.Where(p => !p.IsReview))
                page.IsComplete = IsPageComplete(page.PageNumber);

            if (IsReviewPage)
            {
                BuildReviewItems();
            }
            else
            {
                // Re-evaluate conditional show/hide for the current page once the answer that
                // may gate another question has been persisted server-side.
                var current = _currentIndex >= 0 && _currentIndex < Pages.Count ? Pages[_currentIndex] : null;
                if (current is { IsReview: false } &&
                    _allQuestions.Any(q => q.PageNumber == current.PageNumber && !string.IsNullOrEmpty(q.ConditionJson)))
                {
                    await LoadQuestionsForPageAsync(current.PageNumber);
                }
            }
        });
    }

    private void RecalculateProgress()
    {
        var total = _requiredQuestions.Count;
        var answered = _requiredQuestions.Count(q => _answeredQuestions.Contains(q.Id));
        var pct = total == 0 ? 0d : Math.Round((double)answered / total * 100d, 0);

        Progress = pct;
        ProgressText = $"{pct:0}% ({answered}/{total})";
        if (_run is not null) _run.ProgressPercentage = (int)pct;
    }

    private bool IsPageComplete(int pageNumber)
    {
        var required = _requiredQuestions.Where(q => q.PageNumber == pageNumber).ToList();
        return required.Count > 0 && required.All(q => _answeredQuestions.Contains(q.Id));
    }

    private void BuildReviewItems()
    {
        var unanswered = _requiredQuestions
            .Where(q => !_answeredQuestions.Contains(q.Id))
            .OrderBy(q => q.PageNumber).ThenBy(q => q.Order)
            .Select(q => new AssessmentRunReviewItem
            {
                QuestionId = q.Id,
                Text = q.Question,
                PageNumber = q.PageNumber
            });

        ReviewItems = new ObservableCollection<AssessmentRunReviewItem>(unanswered);
    }

    private async Task PersistRunStateAsync()
    {
        if (_run is null || IsReadOnly) return;
        try
        {
            var dto = new AssessmentRunDto();
            _run.Adapt(dto);
            await Task.Run(() => AssessmentsService.UpdateAssessmentRun(dto));
        }
        catch (Exception ex)
        {
            Logger.Error("Error persisting assessment run state: {0}", ex.Message);
        }
    }

    #endregion

    #region HELPERS

    private static string DecodeAnswerText(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return string.Empty;
        try
        {
            // Drafts are stored as a JSON-encoded string (e.g. "Yes").
            return JsonSerializer.Deserialize<string>(contentJson) ?? contentJson.Trim('"');
        }
        catch
        {
            return contentJson.Trim('"');
        }
    }

    public override void Dispose()
    {
        _saveSubscription?.Dispose();
        _saveSignal.Dispose();
        base.Dispose();
    }

    #endregion
}

public class AssessmentRunReviewItem
{
    public int QuestionId { get; init; }
    public string Text { get; init; } = string.Empty;
    public int PageNumber { get; init; }
}
