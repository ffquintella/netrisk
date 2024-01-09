using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;
using Serilog;
using System;
using Model.DTO;

namespace GUIClient.ViewModels.Assessments;

public class AssessmentRunDialogViewModel : ParameterizedDialogViewModelBaseAsync<AssessmentRunDialogResult,
    AssessmentRunDialogParameter>
{
    #region LANGUAGE

    private string StrDate => Localizer["Date"];
    private string StrAnalyst => Localizer["Analyst"];
    private string StrEntity => Localizer["Entity"] + ":";
    private string StrNewAssessmentRun => Localizer["NewAssessmentRun"];
    private string StrEditAssessmentRun => Localizer["EditAssessmentRun"];
    private string StrMetadata => Localizer["Metadata"];
    private string StrQuestions => Localizer["Questions"];
    private string StrSave => Localizer["Save"];
    private string StrCancel => Localizer["Cancel"];
    private string StrCommit => Localizer["Commit"];
    private string StrQuestion => Localizer["Question"];
    private string StrAnswer => Localizer["Answer"];

    #endregion

    #region PROPERTIES

    private string _strTitle = string.Empty;

    public string StrTitle
    {
        get => _strTitle;
        set => this.RaiseAndSetIfChanged(ref _strTitle, value);
    }

    private ObservableCollection<Entity> _entities = new();

    public ObservableCollection<Entity> Entities
    {
        get => _entities;
        set => this.RaiseAndSetIfChanged(ref _entities, value);
    }

    private ObservableCollection<string> _entityNames = new();

    public ObservableCollection<string> EntityNames
    {
        get => _entityNames;
        set => this.RaiseAndSetIfChanged(ref _entityNames, value);
    }

    private ObservableCollection<AssessmentQuestion> _assessmentQuestions = new();

    public ObservableCollection<AssessmentQuestion> AssessmentQuestions
    {
        get => _assessmentQuestions;
        set => this.RaiseAndSetIfChanged(ref _assessmentQuestions, value);
    }

    private string _selectedEntityName = string.Empty;
    public string SelectedEntityName
    {
        get => _selectedEntityName;
        set => this.RaiseAndSetIfChanged(ref _selectedEntityName, value);
    }

    private bool _isSaveEnabled = false;
    public bool IsSaveEnabled
    {
        get => _isSaveEnabled;
        set => this.RaiseAndSetIfChanged(ref _isSaveEnabled, value);
    }

    #endregion

    #region SERVICES

    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();
    private IAssessmentsService AssessmentsService { get; } = GetService<IAssessmentsService>();
    private IAuthenticationService AuthenticationService { get; } = GetService<IAuthenticationService>();

    #endregion

    #region FIELDS

    private Assessment? _assessment;
    private AssessmentRun? _assessmentRun;
    private List<AssessmentAnswer> SelectedAnswers = new();
    
    private OperationType _operation;

    #endregion

    #region CONSTRUCTORS

    public AssessmentRunDialogViewModel()
    {
        this.ValidationRule(
            view => view.SelectedEntityName,
            name => !string.IsNullOrWhiteSpace(name),
            Localizer["PleaseSelectOneMSG"]);
        
        this.IsValid()
            .Subscribe(x =>
            {
                IsSaveEnabled = x;
            });
    }

    #endregion
    
    #region METHODS

    
    public void BtSaveClicked()
    {
        var analystId = AuthenticationService.AuthenticatedUserInfo!.UserId;
        
        var strEntId = SelectedEntityName.Split(" (")[1].TrimEnd(')');
        var entId = int.Parse(strEntId);
        
        if (_operation == OperationType.Create)
        {

            var assessRun = new AssessmentRunDto()
            {
                AssessmentId = _assessment!.Id,
                EntityId = entId,
                AnalystId = analystId,
                RunDate = DateTime.Now
            };

            var newAssessmentRun = AssessmentsService.CreateAssessmentRun(assessRun);
        

            foreach (var selectedAnswer in SelectedAnswers)
            {
                var answer = new AssessmentRunsAnswerDto()
                {
                    Id = 0,
                    QuestionId = selectedAnswer.QuestionId,
                    AnswerId = selectedAnswer.Id,
                    RunId = newAssessmentRun!.Id
                };
            
                var newAnsw = AssessmentsService.CreateRunAnswer(newAssessmentRun.AssessmentId, answer);
            
                newAssessmentRun.AssessmentRunsAnswers.Add(newAnsw);
            }
        
            var result = new AssessmentRunDialogResult()
            {
                Action = ResultActions.Ok,
                CreatedAssessmentRun = newAssessmentRun
            };

            Close(result);
        }
        else
        {
;
            
            if(_assessmentRun is null) return;


            var assessRun = new AssessmentRunDto()
            {
                Id = _assessmentRun!.Id,
                AssessmentId = _assessment!.Id,
                EntityId = entId,
                AnalystId = analystId,
                RunDate = DateTime.Now
            };

            try
            {
                AssessmentsService.UpdateAssessmentRun(assessRun);
                
                AssessmentsService.DeleteAllAnswers(assessRun.AssessmentId, assessRun.Id);
                
                foreach (var selectedAnswer in SelectedAnswers)
                {
                    var answer = new AssessmentRunsAnswerDto()
                    {
                        Id = 0,
                        QuestionId = selectedAnswer.QuestionId,
                        AnswerId = selectedAnswer.Id,
                        RunId = assessRun!.Id
                    };
            
                    var newAnsw = AssessmentsService.CreateRunAnswer(assessRun.AssessmentId, answer);
            
                    assessRun.AssessmentRunsAnswers.Add(newAnsw);
                }
                
            }catch(Exception e)
            {
                Log.Error(e, "Error updating assessment run");
            }
            
        }

        
    }

    public void BtCancelClicked()
    {
        var result = new AssessmentRunDialogResult()
        {
            Action = ResultActions.Cancel
        };

        Close(result);
    }

    public void ProcessSelectionChange(AssessmentAnswer? answer)
    {
        if(answer is null) return;
        Log.Debug("Changing answer to {Answer}", answer?.Answer);
        
        var anwsr = SelectedAnswers.FirstOrDefault(a => a.QuestionId == answer?.QuestionId);
        if (anwsr is not null)
        {
            SelectedAnswers.Remove(anwsr);
        }
        SelectedAnswers.Add(answer!);
        
    }
    
    public AssessmentAnswer? LoadQuestionAnswer(int questionId)
    {
        var answer = SelectedAnswers.FirstOrDefault(a => a.QuestionId == questionId);
        return answer;
    }
    
    public override Task ActivateAsync(AssessmentRunDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.Invoke( () =>
        {
            if (parameter.Operation == OperationType.Edit)
            {
                StrTitle = StrEditAssessmentRun;

                var run = parameter.AssessmentRun;
                
                _assessmentRun = run;
                
                var answers = AssessmentsService.GetAssessmentRunAnsers(run.AssessmentId, run.Id);
                
                foreach (var answer in answers!)
                {
                    SelectedAnswers.Add(answer.Answer);
                }

            }
            else
            {
                StrTitle = StrNewAssessmentRun;
            }
            
            _operation = parameter.Operation;
            
            Entities = new ObservableCollection<Entity>(EntitiesService.GetAll(null,true));

            foreach (var entity in Entities)
            {
                var entityName = entity.EntitiesProperties.FirstOrDefault(e => e.Type.ToLower() == "name")?.Value ?? string.Empty;
                EntityNames.Add(entityName + " (" + entity.Id + ")");
                
                if(_assessmentRun != null && _assessmentRun.EntityId == entity.Id)
                    SelectedEntityName = entityName + " (" + entity.Id + ")";
            }

            if (parameter.Assessment is null)
            {
                Log.Error("Assessment cannot be null here");
                Close();
            }
            _assessment = parameter.Assessment;
            
            AssessmentQuestions = new ObservableCollection<AssessmentQuestion>(AssessmentsService.GetAssessmentQuestions(_assessment!.Id)!);
            
            
        }, DispatcherPriority.Background,  cancellationToken);
        
        return Task.Run(() => { }, cancellationToken);
    }
    
    
    
    #endregion
}