using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Mapster;
using Avalonia.Controls;
using Avalonia.Threading;
using DAL.Entities;
using ClientServices.Interfaces;
using DAL.EntitiesDto;
using GUIClient.Tools;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class AssessmentQuestionViewModel: ViewModelBase
{
    #region LANGUAGE
    private string StrQuestion { get; }
    private string StrAnswers { get; }
    private string StrAnswer { get; }
    private string StrRisk { get; }
    private string StrSubject { get; }
    private string StrSave { get; }
    private string StrCancel { get; }
    private string TxtQuestion { get; set; } = "";
    
    #endregion
    
    #region PROPERTIES
    private Window DisplayWindow { get; }
    
    private string _txtAnser = "";
    private string TxtAnswer
    {
        get => _txtAnser; 
        set => this.RaiseAndSetIfChanged(ref _txtAnser, value); 
    }

    private float _txtRisk = 0;
    
    private float TxtRisk { 
        get => _txtRisk;
        set
        {
            if (value > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(value), @"0-10.");
            }
            this.RaiseAndSetIfChanged(ref _txtRisk, value);  
        }
    }

    private string _txtSubject = "";
    private string TxtSubject
    {
        get => _txtSubject; 
        set => this.RaiseAndSetIfChanged(ref _txtSubject, value);
        
    }

    private bool _inputEnabled = false;

    private bool InputEnabled
    {
        get => _inputEnabled;
        set=> this.RaiseAndSetIfChanged(ref _inputEnabled, value);
    }
    
    private bool _btSaveEnabled = false;

    private bool BtSaveEnabled
    {
        get => _btSaveEnabled;
        set=> this.RaiseAndSetIfChanged(ref _btSaveEnabled, value);
    }

    private bool _gridEnabled = true;

    private bool GridEnabled
    {
        get => _gridEnabled;
        set=> this.RaiseAndSetIfChanged(ref _gridEnabled, value);
    }
    
    private ObservableCollection<AssessmentAnswer> _answers = new ObservableCollection<AssessmentAnswer>();
    private ObservableCollection<AssessmentAnswer> Answers
    {
        get => _answers;
        set => this.RaiseAndSetIfChanged(ref _answers, value);
    }

    private List<AssessmentAnswer> _answersToDelete = new List<AssessmentAnswer>();
    
    private AssessmentAnswer? _selectedAnswer = null;
    private AssessmentAnswer? SelectedAnswer
    {
        get => _selectedAnswer;
        set
        {
            if (value is not null)
            {
                TxtAnswer = value.Answer;
                TxtRisk = value.RiskScore;
                TxtSubject = System.Text.Encoding.UTF8.GetString(value.RiskSubject);
                BtSaveEnabled = true;
                InputEnabled = true;
            }

            this.RaiseAndSetIfChanged(ref _selectedAnswer, value);
        }
        
    }

    private IAssessmentsService _assessmentsService;
    private Assessment SelectedAssessment { get; }

    public AssessmentQuestion? AssessmentQuestion { get; set; }
    
    public ReactiveCommand<Unit, Unit> BtAddAnswerClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCancelAddAnswerClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteAnswerClicked { get; }
    public ReactiveCommand<bool, Unit> BtSaveAnswerClicked { get; }
    
    public ReactiveCommand<Unit, Unit> BtSaveQuestionClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCancelSaveQuestionClicked { get; }
    
    #endregion
    
    #region SERVICES
    
    
    #endregion
    public AssessmentQuestionViewModel(Window displayWindow, Assessment selectedAssessment, 
       AssessmentQuestion? assessmentQuestion = null, List<AssessmentAnswer>? selectedQuestionAnswers = null) : base()
    {
        DisplayWindow = displayWindow;
        SelectedAssessment = selectedAssessment;
        AssessmentQuestion = assessmentQuestion;
        
        StrQuestion = Localizer["Question"];
        StrAnswers = Localizer["Answers"];
        StrAnswer = Localizer["Answer"];
        StrRisk = Localizer["Risk"];
        StrSubject = Localizer["Subject"];
        StrSave = Localizer["Save"];
        StrCancel = Localizer["Cancel"];
        
        BtAddAnswerClicked = ReactiveCommand.Create(ExecuteAddAnswer);
        BtCancelAddAnswerClicked = ReactiveCommand.Create(ExecuteCancelAddAnswer);
        BtSaveAnswerClicked = ReactiveCommand.Create<bool>(ExecuteSaveAnswer);
        BtDeleteAnswerClicked = ReactiveCommand.Create(ExecuteDeleteAnswer);
        BtSaveQuestionClicked = ReactiveCommand.Create(ExecuteSaveQuestion);
        //BtSaveQuestionClicked = ReactiveCommand.CreateFromTask(ExecuteSaveQuestionAsync);
        BtCancelSaveQuestionClicked = ReactiveCommand.Create(ExecuteCancelSaveQuestion);


        if (AssessmentQuestion is not null && selectedQuestionAnswers is not null)
        {
            TxtQuestion = AssessmentQuestion.Question;
            Answers = new ObservableCollection<AssessmentAnswer>(selectedQuestionAnswers);
        }
        
        

        _assessmentsService = GetService<IAssessmentsService>();

    }
    
    #region METHODS

    private int SaveAnswers()
    {
        // Now let´s save the answers
        var nAnswers = new List<AssessmentAnswer>();
        var upAnswer = new List<AssessmentAnswer>();
        foreach (var answer in Answers )
        {
            if (answer.Id == 0)
            {
                // new answer
                nAnswers.Add(answer);
            }
            else
            {
                // update answer
                upAnswer.Add(answer);
            }
        }


        // first let´s add the new ones
        var crtAnswResult = _assessmentsService
            .CreateAnswers(AssessmentQuestion!.AssessmentId, AssessmentQuestion!.Id, nAnswers);
        if (crtAnswResult.Item1 == 0)
        {
            // Now let´s update the old ones.
            var upAnswResult = _assessmentsService
                .UpdateAnswers(AssessmentQuestion!.AssessmentId, AssessmentQuestion!.Id, upAnswer);
            if (upAnswResult.Item1 == 0)
            {
                //Now let´s delete 
                var resultDel = _assessmentsService.DeleteAnswers(AssessmentQuestion!.AssessmentId, 
                    AssessmentQuestion!.Id,
                    _answersToDelete);
                if(resultDel == 0) return 0;
                else
                {
                    Logger.Error("Error deleting answers");
                    return 1;
                }
            }
            else
            {
                Logger.Error("Error updating new answers.");
                var msgError = MessageBoxManager.GetMessageBoxStandard(
                    new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorSavingAnswersMSG"],
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                msgError.ShowAsync();
                return 1;
            }
        }
        else
        {
            Logger.Error("Error saving new answers.");
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorSavingAnswersMSG"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });
                
            msgError.ShowAsync();
            return 1;
        }
        
    }
    
    private async Task ExecuteSaveQuestionAsync()
    {
        try
        {
            if (AssessmentQuestion is null)
            {
                await CreateQuestionAsync();
            }
            else
            {
                await UpdateQuestionAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving question: {0}", ex.Message);
            await ShowErrorAsync(Localizer["ErrorSavingQuestionMSG"]);
        }
    }
    
    private async Task CreateQuestionAsync()
    {
        var assessmentQuestion = new AssessmentQuestion()
        {
            Question = TxtQuestion,
            AssessmentId = SelectedAssessment.Id
        };

        var result = await _assessmentsService.CreateQuestionAsync(SelectedAssessment.Id, assessmentQuestion);
        switch (result.Item1)
        {
            case 0:
                AssessmentQuestion = result.Item2;
                if (await SaveAnswersAsync() == 0)
                {
                    DisplayWindow.Close();
                }
                break;
            case 1:
                Logger.Error("Error saving question: Question already exists.");
                await ShowErrorAsync(Localizer["QuestionAlreadyExistsMSG"]);
                break;
            case -1:
                Logger.Error("Error saving question: Question already exists.");
                await ShowErrorAsync(Localizer["ErrorSavingQuestionMSG"]);
                break;
        }
    }
    
    private async Task UpdateQuestionAsync()
    {
        AssessmentQuestion!.Question = TxtQuestion;

        var assessmentQuestionDto = new AssessmentQuestionDto();
        AssessmentQuestion.Adapt(assessmentQuestionDto);
                
        var result = await _assessmentsService.UpdateQuestionAsync(SelectedAssessment.Id, assessmentQuestionDto);
        switch (result.Item1)
        {
            case 0:
                AssessmentQuestion = result.Item2;
                if (await SaveAnswersAsync() == 0)
                {
                    DisplayWindow.Close();
                }
                break;
            case 1:
                Logger.Error("Error saving question: Question already exists.");
                await ShowErrorAsync(Localizer["QuestionAlreadyExistsMSG"]);
                break;
            case -1:
                Logger.Error("Error saving question: Question already exists.");
                await ShowErrorAsync(Localizer["ErrorSavingQuestionMSG"]);
                break;
        }
    }

    private async Task<int> SaveAnswersAsync(bool update = false)
    {

        if (update)
        {
            if (TxtAnswer != SelectedAnswer!.Answer && Answers.Any(ans => ans.Answer == TxtAnswer))
            {
                var msgBox1 = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["AnswerAlreadyExistsMSG"],
                        Icon = Icon.Warning,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgBox1.ShowAsync();
                return -1;
            }

            Dispatcher.UIThread.Invoke(() =>
            {
                var editAnwser = SelectedAnswer.DeepCopy();

                editAnwser!.Answer = TxtAnswer;
                editAnwser!.RiskScore = TxtRisk;
                editAnwser!.RiskSubject = Encoding.UTF8.GetBytes(TxtSubject);

                var location = Answers.IndexOf(SelectedAnswer);
                Answers.RemoveAt(location);
                SelectedAnswer = null;
                Answers.Insert(location, editAnwser);
                //Answers.Add(editAnwser);
            });
        }
        else
        {
            if (Answers.Any(ans => ans.Answer == TxtAnswer))
            {
                var msgBox1 = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["AnswerAlreadyExistsMSG"],
                        Icon = Icon.Warning,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgBox1.ShowAsync();
                return -1;
            }

            Dispatcher.UIThread.Invoke(() =>
            {
                var answer = new AssessmentAnswer
                {
                    Answer = TxtAnswer,
                    AssessmentId = SelectedAssessment.Id,
                    QuestionId = -1,
                    RiskScore = TxtRisk,
                    RiskSubject = Encoding.UTF8.GetBytes(TxtSubject)
                };

                Answers.Add(answer);
            });
        }

        ExecuteCancelAddAnswer();

        return 0;
    }
        
    
    
    private void ExecuteSaveQuestion()
    {
        var isUpdate = false;
        if (AssessmentQuestion is not null ) isUpdate = true;
        try
        {
            if (!isUpdate)
            {
                var assessmentQuestion = new AssessmentQuestion()
                {
                    Question = TxtQuestion,
                    AssessmentId = SelectedAssessment.Id
                };


                var result = _assessmentsService.CreateQuestion(SelectedAssessment.Id, assessmentQuestion);
                if (result.Item1 == 0)
                {
                    AssessmentQuestion = result.Item2;
                    if (SaveAnswers() == 0)
                    {
                        DisplayWindow.Close();
                    }
                }

                if (result.Item1 == 1)
                {
                    Logger.Error("Error saving question: Question already exists.");
                    var msgError = MessageBoxManager
                        .GetMessageBoxStandard(   new MessageBoxStandardParams
                        {
                            ContentTitle = Localizer["Error"],
                            ContentMessage = Localizer["QuestionAlreadyExistsMSG"],
                            Icon = Icon.Error,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        });
                            
                    msgError.ShowAsync(); 
                }
                
                if (result.Item1 == -1)
                {
                    Logger.Error("Error saving question: Question already exists.");
                    var msgError =MessageBoxManager
                        .GetMessageBoxStandard(   new MessageBoxStandardParams
                        {
                            ContentTitle = Localizer["Error"],
                            ContentMessage = Localizer["ErrorSavingQuestionMSG"],
                            Icon = Icon.Error,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        });
                            
                    msgError.ShowAsync(); 
                }
                    
            }
            else
            {
                AssessmentQuestion!.Question = TxtQuestion;

                var assessmentQuestionDto = new AssessmentQuestionDto();
                AssessmentQuestion.Adapt(assessmentQuestionDto);
                
                var result = _assessmentsService.UpdateQuestion(SelectedAssessment.Id, assessmentQuestionDto);
                if (result.Item1 == 0)
                {
                    AssessmentQuestion = result.Item2;
                    if (SaveAnswers() == 0)
                    {
                        DisplayWindow.Close();
                    }
                }

                if (result.Item1 == 1)
                {
                    Logger.Error("Error updating question: Question does not exists.");
                    var msgError = MessageBoxManager
                        .GetMessageBoxStandard(   new MessageBoxStandardParams
                        {
                            ContentTitle = Localizer["Error"],
                            ContentMessage = Localizer["QuestionDoesNotExistsMSG"],
                            Icon = Icon.Error,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        });
                            
                    msgError.ShowAsync(); 
                }
                
                if (result.Item1 == -1)
                {
                    Logger.Error("Internal error updating question");
                    var msgError = MessageBoxManager
                        .GetMessageBoxStandard(   new MessageBoxStandardParams
                        {
                            ContentTitle = Localizer["Error"],
                            ContentMessage = Localizer["ErrorSavingQuestionMSG"],
                            Icon = Icon.Error,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        });

                    msgError.ShowAsync();
                }
            }

        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving question: {0}", ex.Message);
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorSavingQuestionMSG"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            msgError.ShowAsync();

        }
    }
    private void ExecuteCancelSaveQuestion()
    {
        DisplayWindow.Close();
    }
    private void ExecuteDeleteAnswer()
    {
        if (SelectedAnswer is null)
        {
            Logger.Error("Button delete answer clicked but no answer selected");
            return;
        }
        _answersToDelete.Add(SelectedAnswer);
        Answers.Remove(SelectedAnswer);
        SelectedAnswer = null;
        GridEnabled = true;
        CleanAndUpdateButtonStatus(false);
    }
    private void ExecuteSaveAnswer(bool update = false)
    {
        if (update)
        {
            if ( TxtAnswer != SelectedAnswer!.Answer && Answers.Any(ans => ans.Answer == TxtAnswer))
            {
                var msgBox1 = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["AnswerAlreadyExistsMSG"],
                        Icon = Icon.Warning,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });
                            
                msgBox1.ShowAsync(); 
                return;  
            }

            var editAnwser = SelectedAnswer.DeepCopy();
            
            editAnwser!.Answer = TxtAnswer;
            editAnwser!.RiskScore = TxtRisk;
            editAnwser!.RiskSubject = Encoding.UTF8.GetBytes(TxtSubject);

            var location = Answers.IndexOf(SelectedAnswer);
            Answers.RemoveAt(location);
            SelectedAnswer = null;
            Answers.Insert(location, editAnwser);
            //Answers.Add(editAnwser);
        }
        else
        {
            if (Answers.Any(ans => ans.Answer == TxtAnswer))
            {
                var msgBox1 = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["AnswerAlreadyExistsMSG"],
                        Icon = Icon.Warning,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });
                            
                msgBox1.ShowAsync(); 
                return;  
            }
            
            var answer = new AssessmentAnswer
            {
                Answer = TxtAnswer,
                AssessmentId = SelectedAssessment.Id,
                QuestionId = -1,
                RiskScore = TxtRisk,
                RiskSubject = Encoding.UTF8.GetBytes(TxtSubject)
            };
        
            Answers.Add(answer);
        }

        ExecuteCancelAddAnswer();
    }
    private void ExecuteAddAnswer()
    {
        SelectedAnswer = null;
        GridEnabled = false;
        CleanAndUpdateButtonStatus(true);
    }
    private void ExecuteCancelAddAnswer()
    {
        SelectedAnswer = null;
        GridEnabled = true;
        CleanAndUpdateButtonStatus(false);
    }
    private void CleanAndUpdateButtonStatus(bool enable)
    {
        TxtAnswer = "";
        TxtRisk = 0;
        TxtSubject = "";
        BtSaveEnabled = enable;
        InputEnabled = enable;
    }
    
    private async Task ShowErrorAsync(string message)
    {
        var msgError = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
        {
            ContentTitle = Localizer["Error"],
            ContentMessage = message,
            Icon = Icon.Error,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        });

        await msgError.ShowAsync();
    }
    
    #endregion
}