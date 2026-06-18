using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.EntitiesDto;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Mapster;
using Model;
using Model.Assessments;
using Model.DTO;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Tools.Security;

namespace GUIClient.ViewModels.Assessments;

public class AssessmentRunViewerViewModel : ParameterizedDialogViewModelBaseAsync<AssessmentRunViewerResult,
    AssessmentRunViewerParameter>
{
    #region LANGUAGE

    public string StrTitle => IsPreview ? Localizer["Preview"] : Localizer["AssessmentRunViewer"];
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
    public string StrSubmit => Localizer["Commit"];
    public string StrPreview => Localizer["Preview"];

    #endregion

    #region SERVICES

    private IAssessmentsService AssessmentsService { get; } = GetService<IAssessmentsService>();
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();

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

    private bool _isPreview;
    public bool IsPreview
    {
        get => _isPreview;
        set => this.RaiseAndSetIfChanged(ref _isPreview, value);
    }

    /// <summary>True only on the Review page of an editable (non-preview, non-submitted) run.</summary>
    private bool _isSubmitVisible;
    public bool IsSubmitVisible
    {
        get => _isSubmitVisible;
        set => this.RaiseAndSetIfChanged(ref _isSubmitVisible, value);
    }

    #endregion

    #region COMMANDS

    public ReactiveCommand<Unit, Unit> PreviousCommand { get; }
    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseViewerCommand { get; }
    public ReactiveCommand<Unit, Unit> SubmitCommand { get; }
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
        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitAsync);
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
        IsPreview = parameter.IsPreview;
        this.RaisePropertyChanged(nameof(StrTitle));

        // A preview only needs an assessment; a real viewer needs both an assessment and a run.
        if (_assessment is null || (!IsPreview && _run is null))
        {
            Logger.Error("Assessment{0} required to open the run viewer", IsPreview ? string.Empty : " and run are");
            Close();
            return;
        }

        IsReadOnly = !IsPreview && _run!.Status == (int)AssessmentStatus.Submitted;

        // Load the question catalogue and predefined answers.
        var questions = AssessmentsService.GetAssessmentQuestions(_assessment.Id) ?? new List<AssessmentQuestion>();
        _allQuestions.Clear();
        _allQuestions.AddRange(questions);
        _requiredQuestions = _allQuestions.Where(q => string.IsNullOrEmpty(q.ConditionJson)).ToList();

        var answers = AssessmentsService.GetAssessmentAnswers(_assessment.Id) ?? new List<AssessmentAnswer>();
        _answersByQuestion.Clear();
        foreach (var group in answers.GroupBy(a => a.QuestionId))
            _answersByQuestion[group.Key] = new ObservableCollection<AssessmentAnswer>(group.OrderBy(a => a.Order));

        // Resume any previously saved draft answers (preview starts blank — nothing persisted).
        if (!IsPreview)
        {
            var drafts = await AssessmentsService.GetDraftAnswersAsync(_run!.Id) ?? new List<AssessmentRunAnswer>();
            foreach (var draft in drafts)
            {
                var text = DecodeAnswerText(draft.AnswerContentJson);
                if (string.IsNullOrEmpty(text)) continue;
                _selectedAnswerText[draft.AssessmentQuestionId] = text;
                _answeredQuestions.Add(draft.AssessmentQuestionId);
            }
        }

        BuildPages();
        RecalculateProgress();

        // Resume at the last visited page if it still exists, otherwise the first.
        var resumePage = _run?.CurrentPageIndex ?? 0;
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

        // Submit (commit) is offered only on the Review page of an editable run — never in
        // preview, never for an already-submitted (read-only) run.
        IsSubmitVisible = page.IsReview && !IsPreview && !IsReadOnly;

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
        // In preview there is no run to evaluate server-side conditional visibility against, so
        // fall back to the full local question set for the page.
        var visible = (IsPreview || _run is null
                          ? null
                          : await AssessmentsService.GetVisibleQuestionsForPageAsync(_run.Id, pageNumber))
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

    #region SUBMIT

    /// <summary>
    /// Commits the run: persists any pending drafts, creates a vulnerability for every selected
    /// answer whose risk score is greater than zero, marks the run as Submitted and closes.
    /// Mirrors the legacy flat-dialog "Enviar" behaviour, but driven by the paged viewer's state.
    /// </summary>
    private async Task SubmitAsync()
    {
        if (IsPreview || _run is null || IsReadOnly) return;

        var confirm = await MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["ConfirmCommitMSG"],
                ButtonDefinitions = ButtonEnum.OkCancel,
                Icon = Icon.Warning,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            }).ShowAsync();

        if (confirm != ButtonResult.Ok) return;

        // Make sure the latest selections are persisted as drafts before committing.
        await FlushPendingAsync();

        try
        {
            foreach (var (questionId, answerText) in _selectedAnswerText)
            {
                if (!_answersByQuestion.TryGetValue(questionId, out var answers)) continue;

                var answer = answers.FirstOrDefault(a =>
                    string.Equals(a.Answer, answerText, StringComparison.OrdinalIgnoreCase));
                if (answer is null || answer.RiskScore <= 0) continue;

                var subject = answer.RiskSubject is { Length: > 0 }
                    ? System.Text.Encoding.UTF8.GetString(answer.RiskSubject)
                    : answer.Answer;

                var vulHashString = answer.Id + answer.AssessmentId + answer.QuestionId + _run.EntityId +
                                    answer.Answer + _run.Id + answer.RiskScore + subject;
                var hash = HashTool.CreateSha1(vulHashString);

                var vuln = new VulnerabilityDto
                {
                    Id = 0,
                    Status = (ushort)IntStatus.New,
                    LastDetection = DateTime.Now,
                    DetectionCount = 1,
                    Title = subject,
                    FirstDetection = DateTime.Now,
                    Severity = Math.Round(answer.RiskScore, 0).ToString(CultureInfo.InvariantCulture),
                    Score = answer.RiskScore,
                    Description = "Created by assessment run: " + _run.Id + " - " + _assessment?.Name + "\n" +
                                  "Answer: " + answer.Answer + "\n" +
                                  "Risk: " + answer.RiskScore + "\n" +
                                  "Subject: " + subject,
                    ImportSource = "assessment",
                    AnalystId = AuthenticationService.AuthenticatedUserInfo!.UserId,
                    ImportHash = hash
                };

                if (_run.HostId != null) vuln.HostId = _run.HostId.Value;

                var nraction = new NrAction
                {
                    DateTime = DateTime.Now,
                    Id = 0,
                    Message = "CREATED BY: " + AuthenticationService.AuthenticatedUserInfo!.UserName,
                    UserId = AuthenticationService.AuthenticatedUserInfo!.UserId,
                    ObjectType = typeof(Vulnerability).Name,
                };

                var newVul = await VulnerabilitiesService.CreateAsync(vuln);
                await VulnerabilitiesService.AddActionAsync(newVul!.Id, nraction.UserId!.Value, nraction);
                Logger.Information("Vulnerability: {Id} created from assessment run {RunId}", newVul.Id, _run.Id);
            }

            _run.Status = (int)AssessmentStatus.Submitted;
            var runDto = new AssessmentRunDto();
            _run.Adapt(runDto);
            await Task.Run(() => AssessmentsService.UpdateAssessmentRun(runDto));

            Close(new AssessmentRunViewerResult { Action = ResultActions.Submitted, RunChanged = true });
        }
        catch (Exception ex)
        {
            Logger.Error("Error submitting assessment run: {Error}", ex.Message);
            await MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorCreatingVulnerabilityMSG"] + " : " + ex.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                }).ShowAsync();
        }
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

        RecalculateProgress();

        // Preview tracks answers locally so progress/review work, but never persists.
        if (IsPreview) return;

        lock (_pendingLock)
        {
            _pendingSaves[item.QuestionId] = contentJson;
        }

        _saveSignal.OnNext(Unit.Default);
    }

    private async Task FlushPendingAsync()
    {
        if (IsPreview || _run is null) return;

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
