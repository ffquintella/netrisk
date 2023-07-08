using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DAL.Entities;
using ClientServices.Interfaces;
using GUIClient.Tools;
using GUIClient.Views;
using MessageBox.Avalonia.DTO;
using MessageBox.Avalonia.Enums;
using Model.Exceptions;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class AssessmentViewModel: ViewModelBase
{
    
    public string StrAssessments { get; }

    private string _strAnswers;
    public string StrAnswers => _strAnswers;
    
    public string StrQuestions { get; }

    private bool _isInitialized = false;
    
    private IAssessmentsService _assessmentsService;
    
    private ObservableCollection<Assessment> _assessments;
    
    public ObservableCollection<Assessment> Assessments
    {
        get => _assessments;
        set => this.RaiseAndSetIfChanged(ref _assessments, value);
    }
    private Assessment? _selectedAssessment;

    public Assessment? SelectedAssessment
    {
        get
        {
            return _selectedAssessment;
        }
        set
        {
            if (value != null)
            {
                switch(SelectedTabIndex)
                {
                    case 0:
                        UpdateSelectedQuestions( value.Id);
                        UpdateSelectedAnswers(value.Id);
                        break;
                }                
            }
            this.RaiseAndSetIfChanged(ref _selectedAssessment, value);
        }
    }

    private AssessmentQuestion? _selectedAssessmentQuestion;
    
    public AssessmentQuestion? SelectedAssessmentQuestion
    {
        get
        {
            return _selectedAssessmentQuestion;
        }
        set
        {
            if (value != null)
            {

                UpdateAssessmentQuestionAnswers( value.Id);
            
            }
            this.RaiseAndSetIfChanged(ref _selectedAssessmentQuestion, value);
        }
    }
    
    public int SelectedTabIndex { get; set; } = 0;

    private ObservableCollection<AssessmentQuestion> _assessmentQuestions;

    public ObservableCollection<AssessmentQuestion> AssessmentQuestions
    {
        get => _assessmentQuestions;
        set => this.RaiseAndSetIfChanged(ref _assessmentQuestions, value);
    }

    private void UpdateSelectedQuestions(int assessmentId)
    {
        var questions = _assessmentsService.GetAssessmentQuestions(assessmentId);
        if (questions == null) return;
        AssessmentQuestions = new ObservableCollection<AssessmentQuestion>(questions);
    }
    
    private ObservableCollection<AssessmentAnswer> _assessmentAnswers;

    public ObservableCollection<AssessmentAnswer> AssessmentAnswers
    {
        get => _assessmentAnswers;
        set => this.RaiseAndSetIfChanged(ref _assessmentAnswers, value);
    }
    
    private void UpdateSelectedAnswers(int assessmentId)
    {
        var answers = _assessmentsService.GetAssessmentAnswers(assessmentId);
        if (answers == null) return;
        AssessmentAnswers = new ObservableCollection<AssessmentAnswer>(answers);
    }
    
    private ObservableCollection<AssessmentAnswer> _assessmentQuestionAnswers;
    public ObservableCollection<AssessmentAnswer> AssessmentQuestionAnswers
    {
        get => _assessmentQuestionAnswers;
        set => this.RaiseAndSetIfChanged(ref _assessmentQuestionAnswers, value);
    }

    private void UpdateAssessmentQuestionAnswers(int assessmentQuestionId)
    {
        AssessmentQuestionAnswers = 
            new ObservableCollection<AssessmentAnswer>(AssessmentAnswers
                .Where(answ => answ.QuestionId == assessmentQuestionId).ToList());
    }

    private bool _assessmentAddBarVisible = false;
    public bool AssessmentAddBarVisible
    {
        get => _assessmentAddBarVisible;
        set => this.RaiseAndSetIfChanged(ref _assessmentAddBarVisible, value);
    }
    public string StrAnswer { get; }
    public string StrRisk { get; }
    public string StrSubject { get; }

    private string _txtAssessmentAddValue = "";
    public string TxtAssessmentAddValue
    {
        get => _txtAssessmentAddValue; 
        set => this.RaiseAndSetIfChanged(ref _txtAssessmentAddValue, value); 
    } 
    public ReactiveCommand<Unit, Unit> BtAddAssessmentClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCancelAddAssessmentClicked { get; }
    
    public ReactiveCommand<Unit, Unit> BtDeleteAssessmentClicked { get; }
    public ReactiveCommand<bool, Unit> BtSaveAssessmentClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteQuestionClicked { get; }
    
    public ReactiveCommand<AssessmentView, Unit> BtAddQuestionClicked { get; }
    public ReactiveCommand<AssessmentView, Unit> BtEditQuestionClicked { get; }
    
    public AssessmentViewModel() : base()
    {
        
        _assessments = new ObservableCollection<Assessment>();
        _assessmentsService = GetService<IAssessmentsService>();
        _assessmentQuestions = new ObservableCollection<AssessmentQuestion>(); 
        _assessmentAnswers = new ObservableCollection<AssessmentAnswer>();
        _assessmentQuestionAnswers = new ObservableCollection<AssessmentAnswer>(); 
        
        StrAssessments = Localizer["Assessments"];
        _strAnswers = Localizer["Answers"];
        StrQuestions = Localizer["Questions"];
        StrAnswer = Localizer["Answer"];
        StrRisk = Localizer["Risk"];
        StrSubject = Localizer["Subject"];
        
        BtAddAssessmentClicked = ReactiveCommand.Create(ExecuteAddAssessment);
        BtCancelAddAssessmentClicked = ReactiveCommand.Create(ExecuteCancelAddAssessment);
        BtSaveAssessmentClicked = ReactiveCommand.Create<bool>(ExecuteSaveAssessment);
        BtDeleteAssessmentClicked = ReactiveCommand.Create(ExecuteDeleteAssessment);
        BtDeleteQuestionClicked = ReactiveCommand.Create(ExecuteDeleteQuestion);
        BtAddQuestionClicked = ReactiveCommand.Create<AssessmentView>(ExecuteAddQuestion);
        BtEditQuestionClicked = ReactiveCommand.Create<AssessmentView>(ExecuteEditQuestion);
        
        AuthenticationService.AuthenticationSucceeded += (obj, args) =>
        {
            Initialize();
        };
    }
    
    private void ExecuteAddAssessment()
    {
        TxtAssessmentAddValue = "";
        AssessmentAddBarVisible = true;
    }
    
    private async void ExecuteDeleteQuestion()
    {
        if(SelectedAssessmentQuestion == null)
        {
            return;
        }
        
        var msgBox1 = MessageBox.Avalonia.MessageBoxManager
            .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["ConfirmDeleteAssessmentQuestionMSG"] + SelectedAssessmentQuestion.Question,
                ButtonDefinitions = ButtonEnum.OkCancel,
                Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            });
                            
        var result = await msgBox1.Show();

        if (result == ButtonResult.Ok)
        {
            var delResult = _assessmentsService.DeleteQuestion(SelectedAssessmentQuestion.AssessmentId, SelectedAssessmentQuestion.Id);
            if (delResult == -1)
            {
                var msgBox2 = MessageBox.Avalonia.MessageBoxManager
                    .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorDeletingAssessmentQuestionMSG"],
                        Icon = MessageBox.Avalonia.Enums.Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    });
                            
                await msgBox2.Show();
                return;
            }
            AssessmentQuestions.Remove(SelectedAssessmentQuestion);
            SelectedAssessmentQuestion = null;
        }
        
    }
    
    private async void ExecuteDeleteAssessment()
    {
        if(SelectedAssessment == null)
        {
            return;
        }
        
        var msgBox1 = MessageBox.Avalonia.MessageBoxManager
            .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["ConfirmDeleteAssessmentMSG"] + SelectedAssessment.Name,
                ButtonDefinitions = ButtonEnum.OkCancel,
                Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            });
                            
        var result = await msgBox1.Show();

        if (result == ButtonResult.Ok)
        {
            var delResult = _assessmentsService.Delete(SelectedAssessment.Id);
            if (delResult == -1)
            {
                var msgBox2 = MessageBox.Avalonia.MessageBoxManager
                    .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorDeletingAssessmentMSG"],
                        Icon = MessageBox.Avalonia.Enums.Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    });
                            
                await msgBox2.Show();
                return;
            }
            Assessments.Remove(SelectedAssessment);
            SelectedAssessment = null;
            AssessmentQuestions = new ObservableCollection<AssessmentQuestion>();
        }
        
    }
    
    public async void ExecuteAddQuestion(AssessmentView parentControl)
    {
        
        var dialog = new AssessmentQuestionView()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        
        dialog.DataContext = new AssessmentQuestionViewModel(dialog, SelectedAssessment!);

        if (Avalonia.Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow != null) await dialog.ShowDialog(desktop.MainWindow);
        }
        
        AssessmentQuestions.Add(((AssessmentQuestionViewModel)dialog.DataContext!).AssessmentQuestion!);
        
        //this.RaiseAndSetIfChanged(ref _selectedAssessmentQuestion, assessmentQuestion);
        
    }
    
    public async void ExecuteEditQuestion(AssessmentView parentControl)
    {

        if (_selectedAssessmentQuestion == null) throw new Exception("_selectedAssessmentQuestion cannot be null here");
        
        var dialog = new AssessmentQuestionView()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        
        dialog.DataContext = new AssessmentQuestionViewModel(dialog, SelectedAssessment!, 
            _selectedAssessmentQuestion!, AssessmentQuestionAnswers.ToList());
        
        int index = AssessmentQuestions.IndexOf(_selectedAssessmentQuestion!);
        
        if (Avalonia.Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow != null) await dialog.ShowDialog(desktop.MainWindow);
        }
        //await dialog.ShowDialog( parentControl.ParentWindow );

        var saq = ((AssessmentQuestionViewModel)dialog!.DataContext!).AssessmentQuestion!.DeepCopy();

        AssessmentQuestions.RemoveAt(index);
        AssessmentQuestions.Insert(index, saq!);
        SelectedAssessmentQuestion = AssessmentQuestions[index];

    }
    
    private void ExecuteCancelAddAssessment()
    {
        TxtAssessmentAddValue = "";
        AssessmentAddBarVisible = false;
    }
    
    private async void ExecuteSaveAssessment(bool update = false)
    {
        if(TxtAssessmentAddValue.Trim() == "")
        {
            var msgBox1 = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["AssessmentNameInvalidMSG"],
                    Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });
                            
            await msgBox1.Show(); 
            return;
        }
        
        if(Assessments.FirstOrDefault(ass => ass.Name == TxtAssessmentAddValue) != null)
        {
            var msgBox2 = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["AssessmentNameExistsMSG"],
                    Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });
                            
            await msgBox2.Show(); 
            return;
        }

        var result = _assessmentsService.Create(new Assessment
        {
            Name = TxtAssessmentAddValue,
        });

        if (result.Item1 == 0)
        {
            Assessments.Add(result.Item2!);
            TxtAssessmentAddValue = "";
            AssessmentAddBarVisible = false;
            return ;
        }

        var msgBox3 = MessageBox.Avalonia.MessageBoxManager
            .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["ErrorCreatingAssessmentMSG"],
                Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });
                            
        await msgBox3.Show(); 
        return;
        
        //TxtAssessmentAddValue = "";
        //AssessmentAddBarVisible = false;
    }
    
    private void Initialize()
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            var assessments = _assessmentsService.GetAssessments();
            if (assessments == null)
            {
                Logger.Error("Assessments are null");
                throw new RestComunicationException("Error getting assessments");
            }
            Assessments = new ObservableCollection<Assessment>(assessments);
            
        }
    }
    
    
}