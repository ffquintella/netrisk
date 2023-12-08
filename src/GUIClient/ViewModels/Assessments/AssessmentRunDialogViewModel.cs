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
    private List<AssessmentAnswer> SelectedAnswers = new();

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

        var assessRun = new AssessmentRun()
        {
            AssessmentId = _assessment!.Id,
            EntityId = entId,
            AnalystId = analystId,
            RunDate = DateTime.Now
        };
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
    
    public override Task ActivateAsync(AssessmentRunDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.Invoke( () =>
        {
            if (parameter.Operation == OperationType.Edit)
            {
                StrTitle = StrEditAssessmentRun;
            }
            else
            {
                StrTitle = StrNewAssessmentRun;
            }
            
            Entities = new ObservableCollection<Entity>(EntitiesService.GetAll(null,true));

            foreach (var entity in Entities)
            {
                var entityName = entity.EntitiesProperties.FirstOrDefault(e => e.Type.ToLower() == "name")?.Value ?? string.Empty;
                EntityNames.Add(entityName + " (" + entity.Id + ")");
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