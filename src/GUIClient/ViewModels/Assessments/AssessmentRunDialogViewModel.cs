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
using System.Globalization;
using AutoMapper;
using Avalonia.Controls;
using DAL.EntitiesDto;
using Model;
using Model.Assessments;
using Model.DTO;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using Tools.Security;

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
    private string StrComments => Localizer["Comments"]+ ":";
    private string StrHost => Localizer["Host"]+ ":";

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
    
    private ObservableCollection<Host> _hosts = new();

    public ObservableCollection<Host> Hosts
    {
        get => _hosts;
        set => this.RaiseAndSetIfChanged(ref _hosts, value);
    }
    
    private ObservableCollection<string> _hostNames = new();

    public ObservableCollection<string> HostNames
    {
        get => _hostNames;
        set => this.RaiseAndSetIfChanged(ref _hostNames, value);
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
    
    private string _selectedHostName = string.Empty;
    public string SelectedHostName
    {
        get => _selectedHostName;
        set => this.RaiseAndSetIfChanged(ref _selectedHostName, value);
    }

    private bool _isSaveEnabled = false;
    public bool IsSaveEnabled
    {
        get => _isSaveEnabled;
        set => this.RaiseAndSetIfChanged(ref _isSaveEnabled, value);
    }

    private string? _comments = string.Empty;
    public string? Comments
    {
        get => _comments;
        set => this.RaiseAndSetIfChanged(ref _comments, value);
    }
    
    #endregion

    #region SERVICES

    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();
    private IHostsService HostsService { get; } = GetService<IHostsService>();
    private IAssessmentsService AssessmentsService { get; } = GetService<IAssessmentsService>();
    //private IAuthenticationService AuthenticationService { get; } = GetService<IAuthenticationService>();
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    private IMapper Mapper { get; } = GetService<IMapper>();

    #endregion

    #region FIELDS

    private Assessment? _assessment;
    private AssessmentRun? _assessmentRun;
    private List<AssessmentAnswer> _selectedAnswers = new();
    
    private OperationType _operation;

    #endregion

    #region CONSTRUCTORS

    public AssessmentRunDialogViewModel()
    {
        this.ValidationRule(
            view => view.SelectedEntityName,
            name => !string.IsNullOrWhiteSpace(name),
            Localizer["PleaseSelectOneMSG"]);
        
        /*this.ValidationRule(
            view => view.SelectedHostName,
            name => !(string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(SelectedEntityName)),
            Localizer["PleaseSelectOneMSG"]);*/
        
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
        
        var strHostId = SelectedHostName.Split(" (")[1].TrimEnd(')');
        var hostId = int.Parse(strHostId);
        
        if (_operation == OperationType.Create)
        {

            var assessRun = new AssessmentRunDto()
            {
                AssessmentId = _assessment!.Id,
                EntityId = entId,
                HostId = hostId,
                AnalystId = analystId,
                Comments = Comments,
                RunDate = DateTime.Now,
                Status = (int) AssessmentStatus.Open
            };

            var newAssessmentRun = AssessmentsService.CreateAssessmentRun(assessRun);
        

            foreach (var selectedAnswer in _selectedAnswers)
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
                HostId = hostId,
                Comments = Comments,
                RunDate = DateTime.Now,
                Status = (int) AssessmentStatus.Open
            };

            try
            {
                AssessmentsService.UpdateAssessmentRun(assessRun);
                
                AssessmentsService.DeleteAllAnswers(assessRun.AssessmentId, assessRun.Id);
                
                _selectedAnswers = _selectedAnswers.OrderBy(sa => sa.Id).GroupBy(sa => sa.QuestionId).Select(g => g.Last()).ToList();
                
                foreach (var selectedAnswer in _selectedAnswers)
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
                
                var result = new AssessmentRunDialogResult()
                {
                    Action = ResultActions.Ok
                };

                Close(result);
                
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

    public async void BtCommitClicked()
    {
        var msgBox1 = MessageBoxManager
            .GetMessageBoxStandard(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["ConfirmCommitMSG"] ,
                ButtonDefinitions = ButtonEnum.OkCancel,
                Icon = Icon.Warning,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            });
                            
        var result = await msgBox1.ShowAsync();

        if (result == ButtonResult.Ok)
        {
            // Now we will create a new vulnerability for each answer with risk > 0
            
            try
            {
                foreach (var answer in _selectedAnswers)
                {
                    if (answer.RiskScore > 0)
                    {

                        if(_assessmentRun is null) return;
                        
                        var vulHashString = answer.Id + answer.AssessmentId + SelectedEntityName + answer.QuestionId + _assessmentRun.EntityId +
                                            answer.Answer + _assessmentRun.Id + answer.RiskScore + answer.RiskSubject;
                        var hash = HashTool.CreateSha1(vulHashString);
                        
                        var vuln = new VulnerabilityDto()
                        {
                            Id = 0,
                            Status = (ushort)IntStatus.New,
                            LastDetection = DateTime.Now,
                            DetectionCount = 1,
                            Title = System.Text.Encoding.UTF8.GetString(answer.RiskSubject),
                            FirstDetection = DateTime.Now,
                            Severity = Math.Round(answer.RiskScore, 0).ToString(CultureInfo.InvariantCulture),
                            Score = answer.RiskScore,
                            Description = "Created by assessment run: " + _assessmentRun!.Id + " - " + _assessmentRun.Assessment!.Name +  "\n" +
                                          "Answer: " + answer.Answer + "\n" +
                                          "Risk: " + answer.RiskScore + "\n" +
                                          "Subject: " + System.Text.Encoding.UTF8.GetString(answer.RiskSubject),
                           ImportSource = "assessment",
                           AnalystId = AuthenticationService.AuthenticatedUserInfo!.UserId,
                           ImportHash = hash
                           
                        };
                        
                        if(_assessmentRun.HostId != null)
                            vuln.HostId = _assessmentRun.HostId.Value;
                        
                        var nraction = new NrAction()
                        {
                            DateTime = DateTime.Now,
                            Id = 0,
                            Message = "CREATED BY: " + AuthenticationService.AuthenticatedUserInfo!.UserName,
                            UserId = AuthenticationService.AuthenticatedUserInfo!.UserId,
                            ObjectType = typeof(Vulnerability).Name,
                        };

                        var newVul = await VulnerabilitiesService.CreateAsync(vuln);
                        await VulnerabilitiesService.AddActionAsync(newVul!.Id, nraction.UserId!.Value, nraction);
                        
                        Logger.Information("Vulnerbility: {Id} created", newVul.Id);

                    }
                }

                _assessmentRun!.Status = (int)AssessmentStatus.Submitted;

                var runDto = new AssessmentRunDto();
                
                Mapper.Map(_assessmentRun, runDto); 
                AssessmentsService.UpdateAssessmentRun(runDto);
                
               
                var ard = new AssessmentRunDialogResult()
                {
                    Action = ResultActions.Submitted
                };

                Close(ard);
                
            }
            catch (Exception ex)
            {
                Logger.Error("Error creating vulnerability: {Error}", ex.Message);
                var msgError = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorCreatingVulnerabilityMSG"] + " : " + ex.Message,
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });
                            
                await msgError.ShowAsync(); 
            }

            
        }
    }

    public void ProcessSelectionChange(AssessmentAnswer? answer)
    {
        if(answer is null) return;
        Log.Debug("Changing answer to {Answer}", answer?.Answer);
        
        var anwsr = _selectedAnswers.FirstOrDefault(a => a.QuestionId == answer?.QuestionId);
        if (anwsr is not null)
        {
            _selectedAnswers.Remove(anwsr);
        }
        _selectedAnswers.Add(answer!);
        
    }
    
    public AssessmentAnswer? LoadQuestionAnswer(int questionId)
    {
        var answer = _selectedAnswers.FirstOrDefault(a => a.QuestionId == questionId);
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
                
                Comments = run!.Comments;
                
                var answers = AssessmentsService.GetAssessmentRunAnsers(run.AssessmentId, run.Id);
                
                foreach (var answer in answers!)
                {
                    _selectedAnswers.Add(answer.Answer);
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
            
            Hosts = new ObservableCollection<Host>(HostsService.GetAll());

            foreach (var host in Hosts)
            {
                var hostName = host.HostName ?? string.Empty;
                HostNames.Add(hostName + " (" + host.Id + ")");
                
                if(_assessmentRun != null && _assessmentRun.HostId == host.Id)
                    SelectedHostName = hostName + " (" + host.Id + ")";
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