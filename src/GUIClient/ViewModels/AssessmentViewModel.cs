using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DAL.Entities;
using ClientServices.Interfaces;
using GUIClient.Tools;
using GUIClient.ViewModels.Assessments;
using GUIClient.Views;
using MsBox.Avalonia.Enums;
using Model.Exceptions;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class AssessmentViewModel: ViewModelBase
{
    #region LANGUAGES
    public string StrAssessments { get; }
    private string _strAnswers;
    public string StrAnswers => _strAnswers;
    
    public string StrAnswer { get; } = Localizer["Answer"];
    public string StrRisk { get; }
    public string StrSubject { get; }
    public string StrQuestions { get; }
    
    public string StrAssessmentsRuns { get; } = Localizer["AssessmentsRuns"];
    #endregion
    
    #region PROPERTIES

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
                    case 1:
                        AssessmentsRunsListViewModel = new AssessmentsRunsListViewModel(value);
                        break;
                }                
            }
            else
            {
                if (value != null) AssessmentsRunsListViewModel = new AssessmentsRunsListViewModel(value);
            }
            this.RaiseAndSetIfChanged(ref _selectedAssessment, value);
        }
    }
    
    private ObservableCollection<AssessmentAnswer> _assessmentAnswers;

    public ObservableCollection<AssessmentAnswer> AssessmentAnswers
    {
        get => _assessmentAnswers;
        set => this.RaiseAndSetIfChanged(ref _assessmentAnswers, value);
    }
    
    private AssessmentsRunsListViewModel _assessmentsRunsListViewModel = new();

    public AssessmentsRunsListViewModel AssessmentsRunsListViewModel
    {
        get => _assessmentsRunsListViewModel;
        set => this.RaiseAndSetIfChanged(ref _assessmentsRunsListViewModel, value);
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
    
    private int _selectedTabIndex = 0;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            SelectedAssessment = null;
            this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
        }
    } 

    private ObservableCollection<AssessmentQuestion> _assessmentQuestions;

    public ObservableCollection<AssessmentQuestion> AssessmentQuestions
    {
        get => _assessmentQuestions;
        set => this.RaiseAndSetIfChanged(ref _assessmentQuestions, value);
    }
    
    private string _txtAssessmentValue = "";
    public string TxtAssessmentValue
    {
        get => _txtAssessmentValue; 
        set => this.RaiseAndSetIfChanged(ref _txtAssessmentValue, value); 
    } 
    
    private bool _updateOperation = true;
    public bool UpdateOperation
    {
        get => _updateOperation; 
        set => this.RaiseAndSetIfChanged(ref _updateOperation, value); 
    }
    
    #endregion
    
    #region BUTTONS
    
    public ReactiveCommand<Unit, Unit> BtAddAssessmentClicked { get; }
    public ReactiveCommand<Unit, Unit> BtEditAssessmentClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCancelAddAssessmentClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteAssessmentClicked { get; }
    public ReactiveCommand<bool, Unit> BtSaveAssessmentClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteQuestionClicked { get; }
    public ReactiveCommand<AssessmentView, Unit> BtAddQuestionClicked { get; }
    public ReactiveCommand<AssessmentView, Unit> BtEditQuestionClicked { get; }
    
    #endregion

    #region CONSTRUCTOR
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
        StrRisk = Localizer["Risk"];
        StrSubject = Localizer["Subject"];
        
        BtAddAssessmentClicked = ReactiveCommand.Create(ExecuteAddAssessment);
        BtEditAssessmentClicked = ReactiveCommand.Create(ExecuteEditAssessment);
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
    #endregion

    #region METHODS

      
    private void UpdateSelectedQuestions(int assessmentId)
    {
        var questions = _assessmentsService.GetAssessmentQuestions(assessmentId);
        if (questions == null) return;
        AssessmentQuestions = new ObservableCollection<AssessmentQuestion>(questions);
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

    private bool _assessmentAddEditBarVisible = false;
    public bool AssessmentAddEditBarVisible
    {
        get => _assessmentAddEditBarVisible;
        set => this.RaiseAndSetIfChanged(ref _assessmentAddEditBarVisible, value);
    }

    
    private void ExecuteAddAssessment()
    {
        UpdateOperation = false; // add
        TxtAssessmentValue = "";
        AssessmentAddEditBarVisible = true;
    }
    
    private void ExecuteEditAssessment()
    {
        UpdateOperation = true; // edit
        TxtAssessmentValue = SelectedAssessment!.Name;
        AssessmentAddEditBarVisible = true;
    }
    
    
    private async void ExecuteDeleteQuestion()
    {
        if(SelectedAssessmentQuestion == null)
        {
            return;
        }
        
        var msgBox1 = MessageBoxManager
            .GetMessageBoxStandard(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["ConfirmDeleteAssessmentQuestionMSG"] + SelectedAssessmentQuestion.Question,
                ButtonDefinitions = ButtonEnum.OkCancel,
                Icon = Icon.Warning,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            });
                            
        var result = await msgBox1.ShowAsync();

        if (result == ButtonResult.Ok)
        {
            var delResult = _assessmentsService.DeleteQuestion(SelectedAssessmentQuestion.AssessmentId, SelectedAssessmentQuestion.Id);
            if (delResult == -1)
            {
                var msgBox2 = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorDeletingAssessmentQuestionMSG"],
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    });
                            
                await msgBox2.ShowAsync();
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
        
        var msgBox1 = MessageBoxManager
            .GetMessageBoxStandard(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["ConfirmDeleteAssessmentMSG"] + SelectedAssessment.Name,
                ButtonDefinitions = ButtonEnum.OkCancel,
                Icon = Icon.Warning,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            });
                            
        var result = await msgBox1.ShowAsync();

        if (result == ButtonResult.Ok)
        {
            var delResult = _assessmentsService.Delete(SelectedAssessment.Id);
            if (delResult == -1)
            {
                var msgBox2 = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorDeletingAssessmentMSG"],
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    });
                            
                await msgBox2.ShowAsync();
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
        TxtAssessmentValue = "";
        AssessmentAddEditBarVisible = false;
    }

    private async void ExecuteSaveAssessment(bool update = false)
    {
        if (update && SelectedAssessment!.Name == TxtAssessmentValue)
        {
            AssessmentAddEditBarVisible = false;
            return;
        }

        if (TxtAssessmentValue.Trim() == "")
        {
            var msgBox1 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["AssessmentNameInvalidMSG"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox1.ShowAsync();
            return;
        }


        if (Assessments.FirstOrDefault(ass => ass.Name == TxtAssessmentValue) != null)
        {
            var msgBox2 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["AssessmentNameExistsMSG"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox2.ShowAsync();
            return;
        }


        if (!update)
        {
            var result = await _assessmentsService.CreateAsync(new Assessment
            {
                Name = TxtAssessmentValue,
            });

            if (result.Item1 == 0)
            {
                Assessments.Add(result.Item2!);
                TxtAssessmentValue = "";
                AssessmentAddEditBarVisible = false;
                return;
            }

            var msgBox3 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["ErrorCreatingAssessmentMSG"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox3.ShowAsync();
        }
        else
        {
            var updatedAssessment = SelectedAssessment!.DeepCopy();
            updatedAssessment!.Name = TxtAssessmentValue;
            
            var result = await _assessmentsService.UpdateAsync(updatedAssessment);

            if (result == 0)
            {
                SelectedAssessment!.Name = TxtAssessmentValue;
                
                Assessments.FirstOrDefault(a => a.Id == SelectedAssessment.Id)!.Name = TxtAssessmentValue;

                var tmAssList = Assessments;
                Assessments = new ObservableCollection<Assessment>();
                Assessments = tmAssList;
                
                TxtAssessmentValue = "";
                AssessmentAddEditBarVisible = false;
                return;
            }

            var msgBox3 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["ErrorUpdatingAssessmentMSG"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox3.ShowAsync();
        }
        
    }
    
    private void Initialize()
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            Task.Run(async () =>
            {
                var assessments = await _assessmentsService.GetAssessmentsAsync();
                if (assessments == null)
                {
                    Logger.Error("Assessments are null");
                    throw new RestComunicationException("Error getting assessments");
                }
                Assessments = new ObservableCollection<Assessment>(assessments);
            });

            
        }
    }

    #endregion
    
}