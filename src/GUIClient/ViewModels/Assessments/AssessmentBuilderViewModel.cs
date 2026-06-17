using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.EntitiesDto;
using Mapster;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

/// <summary>
/// Inline, card-based questionnaire builder for a single assessment. Replaces the
/// grid + modal-dialog authoring flow with a single scrolling column of question cards
/// grouped by page, editable in place — modeled on Google Forms / Jotform builders.
/// </summary>
public class AssessmentBuilderViewModel : ViewModelBase
{
    #region LANGUAGE
    public string StrAddQuestion => Localizer["AddQuestion"];
    public string StrAddPage => Localizer["AddPage"];
    public string StrQuestion => Localizer["Question"];
    public string StrPage => Localizer["Page"];
    public string StrOrder => Localizer["Order"];
    public string StrExplanation => Localizer["Explanation"];
    public string StrAnswers => Localizer["Answers"];
    public string StrAnswer => Localizer["Answer"];
    public string StrRisk => Localizer["Risk"];
    public string StrSubject => Localizer["Subject"];
    public string StrAddOption => Localizer["AddOption"];
    public string StrShowOnlyIf => Localizer["ShowOnlyIfMSG"];
    public string StrPreview => Localizer["Preview"];
    public string StrNoQuestionsMSG => Localizer["NoQuestionsMSG"];
    #endregion

    private IAssessmentsService AssessmentsService { get; } = GetService<IAssessmentsService>();

    private readonly Assessment _assessment;

    private ObservableCollection<AssessmentQuestionCardViewModel> _questions = new();
    public ObservableCollection<AssessmentQuestionCardViewModel> Questions
    {
        get => _questions;
        set => this.RaiseAndSetIfChanged(ref _questions, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool HasQuestions => Questions.Count > 0;

    public ReactiveCommand<Unit, Unit> AddQuestionCommand { get; }
    public ReactiveCommand<Unit, Unit> AddPageCommand { get; }

    public AssessmentBuilderViewModel() : this(new Assessment { Id = 0, Name = "" }) { }

    public AssessmentBuilderViewModel(Assessment assessment)
    {
        _assessment = assessment;
        AddQuestionCommand = ReactiveCommand.Create(() => AddCard(NextPage(onNewPage: false)));
        AddPageCommand = ReactiveCommand.Create(() => AddCard(NextPage(onNewPage: true)));
        if (_assessment.Id > 0) _ = LoadAsync();
    }

    #region LOAD

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var (questions, answers) = await Task.Run(() =>
            {
                var q = AssessmentsService.GetAssessmentQuestions(_assessment.Id) ?? new List<AssessmentQuestion>();
                var a = AssessmentsService.GetAssessmentAnswers(_assessment.Id) ?? new List<AssessmentAnswer>();
                return (q, a);
            });

            var answersByQuestion = answers.GroupBy(a => a.QuestionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var cards = questions
                .OrderBy(q => q.PageNumber).ThenBy(q => q.Order)
                .Select(q => new AssessmentQuestionCardViewModel(this, _assessment.Id, q,
                    answersByQuestion.TryGetValue(q.Id, out var qa) ? qa : null))
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Questions = new ObservableCollection<AssessmentQuestionCardViewModel>(cards);
                this.RaisePropertyChanged(nameof(HasQuestions));
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Error loading assessment builder: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region EDIT LIFECYCLE

    public void BeginEdit(AssessmentQuestionCardViewModel card)
    {
        foreach (var c in Questions) c.IsEditing = ReferenceEquals(c, card);
        card.RefreshConditionSources(Questions);
    }

    public void CancelEdit(AssessmentQuestionCardViewModel card)
    {
        // A never-saved card is simply discarded; an edited existing card is reloaded.
        if (card.Id == 0)
        {
            Questions.Remove(card);
            this.RaisePropertyChanged(nameof(HasQuestions));
        }
        else
        {
            _ = LoadAsync();
        }
    }

    private void AddCard(int page)
    {
        var orderOnPage = Questions.Count(q => q.Page == page);
        var card = new AssessmentQuestionCardViewModel(this, _assessment.Id, null, null)
        {
            Page = page,
            Order = orderOnPage,
            IsEditing = true
        };
        foreach (var c in Questions) c.IsEditing = false;
        Questions.Add(card);
        ResortInMemory();
        card.RefreshConditionSources(Questions);
        this.RaisePropertyChanged(nameof(HasQuestions));
    }

    private int NextPage(bool onNewPage)
    {
        var maxPage = Questions.Count == 0 ? 0 : Questions.Max(q => q.Page);
        if (onNewPage) return maxPage + 1;
        return maxPage < 1 ? 1 : maxPage;
    }

    #endregion

    #region PERSISTENCE

    public async Task SaveCardAsync(AssessmentQuestionCardViewModel card)
    {
        if (string.IsNullOrWhiteSpace(card.QuestionText))
        {
            await ShowErrorAsync(Localizer["QuestionTextRequiredMSG"]);
            return;
        }

        IsBusy = true;
        try
        {
            var success = await Task.Run(() => PersistCard(card));
            if (success) await LoadAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Error saving question: {0}", ex.Message);
            await ShowErrorAsync(Localizer["ErrorSavingQuestionMSG"]);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool PersistCard(AssessmentQuestionCardViewModel card)
    {
        int questionId;

        if (card.Id == 0)
        {
            var entity = new AssessmentQuestion
            {
                AssessmentId = _assessment.Id,
                Question = card.QuestionText,
                PageNumber = card.Page,
                Order = card.Order,
                ExplanationMarkdown = string.IsNullOrWhiteSpace(card.Explanation) ? null : card.Explanation,
                ConditionJson = card.BuildConditionJson()
            };
            var result = AssessmentsService.CreateQuestion(_assessment.Id, entity);
            if (result.Item1 != 0 || result.Item2 is null) return false;
            questionId = result.Item2.Id;
            card.SetId(questionId);
        }
        else
        {
            var entity = new AssessmentQuestion
            {
                Id = card.Id,
                AssessmentId = _assessment.Id,
                Question = card.QuestionText,
                PageNumber = card.Page,
                Order = card.Order,
                ExplanationMarkdown = string.IsNullOrWhiteSpace(card.Explanation) ? null : card.Explanation,
                ConditionJson = card.BuildConditionJson()
            };
            var dto = new AssessmentQuestionDto();
            entity.Adapt(dto);
            var result = AssessmentsService.UpdateQuestion(_assessment.Id, dto);
            if (result.Item1 != 0) return false;
            questionId = card.Id;
        }

        return PersistOptions(card, questionId);
    }

    private bool PersistOptions(AssessmentQuestionCardViewModel card, int questionId)
    {
        var newOptions = card.Options.Where(o => o.Id == 0)
            .Select(o => o.ToEntity(_assessment.Id, questionId)).ToList();
        var existingOptions = card.Options.Where(o => o.Id > 0)
            .Select(o => o.ToEntity(_assessment.Id, questionId)).ToList();
        var removedOptions = card.RemovedOptions
            .Select(o => o.ToEntity(_assessment.Id, questionId)).ToList();

        if (AssessmentsService.CreateAnswers(_assessment.Id, questionId, newOptions).Item1 != 0) return false;
        if (AssessmentsService.UpdateAnswers(_assessment.Id, questionId, existingOptions).Item1 != 0) return false;
        if (removedOptions.Count > 0 && AssessmentsService.DeleteAnswers(_assessment.Id, questionId, removedOptions) != 0) return false;

        return true;
    }

    public async Task DeleteCardAsync(AssessmentQuestionCardViewModel card)
    {
        var confirm = await MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
        {
            ContentTitle = Localizer["Warning"],
            ContentMessage = Localizer["ConfirmDeleteAssessmentQuestionMSG"] + " " + card.DisplayText,
            ButtonDefinitions = ButtonEnum.OkCancel,
            Icon = Icon.Warning
        }).ShowAsync();

        if (confirm != ButtonResult.Ok) return;

        if (card.Id == 0)
        {
            Questions.Remove(card);
            this.RaisePropertyChanged(nameof(HasQuestions));
            return;
        }

        IsBusy = true;
        try
        {
            var ok = await Task.Run(() => AssessmentsService.DeleteQuestion(_assessment.Id, card.Id) == 0);
            if (ok) await LoadAsync();
            else await ShowErrorAsync(Localizer["ErrorDeletingAssessmentQuestionMSG"]);
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region REORDER

    public void MoveUp(AssessmentQuestionCardViewModel card) => Move(card, -1);
    public void MoveDown(AssessmentQuestionCardViewModel card) => Move(card, +1);

    private void Move(AssessmentQuestionCardViewModel card, int direction)
    {
        var samePage = Questions.Where(q => q.Page == card.Page)
            .OrderBy(q => q.Order).ToList();
        var idx = samePage.IndexOf(card);
        var target = idx + direction;
        if (idx < 0 || target < 0 || target >= samePage.Count) return;

        (samePage[idx], samePage[target]) = (samePage[target], samePage[idx]);

        // Renumber the page 0..n and persist the questions whose order changed.
        var changed = new List<AssessmentQuestionCardViewModel>();
        for (var i = 0; i < samePage.Count; i++)
        {
            if (samePage[i].Order != i)
            {
                samePage[i].Order = i;
                if (samePage[i].Id > 0) changed.Add(samePage[i]);
            }
        }

        ResortInMemory();

        if (changed.Count == 0) return;
        IsBusy = true;
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var c in changed)
                {
                    var dto = new AssessmentQuestionDto();
                    new AssessmentQuestion
                    {
                        Id = c.Id,
                        AssessmentId = _assessment.Id,
                        Question = c.QuestionText,
                        PageNumber = c.Page,
                        Order = c.Order,
                        ExplanationMarkdown = string.IsNullOrWhiteSpace(c.Explanation) ? null : c.Explanation,
                        ConditionJson = c.BuildConditionJson()
                    }.Adapt(dto);
                    AssessmentsService.UpdateQuestion(_assessment.Id, dto);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error reordering questions: {0}", ex.Message);
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsBusy = false);
            }
        });
    }

    private void ResortInMemory()
    {
        var ordered = Questions.OrderBy(q => q.Page).ThenBy(q => q.Order).ToList();
        Questions = new ObservableCollection<AssessmentQuestionCardViewModel>(ordered);
    }

    #endregion

    private static async Task ShowErrorAsync(string message)
    {
        await MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
        {
            ContentTitle = Localizer["Error"],
            ContentMessage = message,
            Icon = Icon.Error
        }).ShowAsync();
    }
}
