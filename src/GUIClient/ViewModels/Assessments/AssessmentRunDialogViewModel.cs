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
using Serilog;
using System;
using System.Globalization;
using Mapster;
using Avalonia.Controls;
using DAL.EntitiesDto;
using Model;
using Model.Assessments;
using Model.DTO;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using Tools.Security;
using Tools.String;

namespace GUIClient.ViewModels.Assessments;

public class AssessmentRunDialogViewModel : ParameterizedDialogViewModelBaseAsync<AssessmentRunDialogResult,
    AssessmentRunDialogParameter>
{
    #region LANGUAGE

    private string StrDate => Localizer["Date"];
    private string StrAnalyst => Localizer["Analyst"];
    public string StrEntity => Localizer["Entity"] + ":";
    private string StrNewAssessmentRun => Localizer["NewAssessmentRun"];
    private string StrEditAssessmentRun => Localizer["EditAssessmentRun"];
    public string StrMetadata => Localizer["Metadata"];
    public new string StrSave => Localizer["Save"];
    public new string StrCancel => Localizer["Cancel"];
    public string StrComments => Localizer["Comments"]+ ":";
    public string StrHost => Localizer["Host"]+ ":";

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

    #endregion

    #region FIELDS

    private Assessment? _assessment;
    private AssessmentRun? _assessmentRun;

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

        if (!LabelIdParser.TryParseTrailingId(SelectedEntityName, out var entId))
        {
            Log.Error("Could not parse entity id from selection: {Selection}", SelectedEntityName);
            return;
        }

        int? hostId = null;

        if (!string.IsNullOrWhiteSpace(SelectedHostName))
        {
            if (!LabelIdParser.TryParseTrailingId(SelectedHostName, out var parsedHostId))
            {
                Log.Error("Could not parse host id from selection: {Selection}", SelectedHostName);
                return;
            }
            hostId = parsedHostId;
        }
        
        // The dialog only captures run metadata; answering/editing the questionnaire happens in
        // the paged AssessmentRunViewer that the caller opens after this dialog returns Ok.
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

            var result = new AssessmentRunDialogResult()
            {
                Action = ResultActions.Ok,
                CreatedAssessmentRun = newAssessmentRun
            };

            Close(result);
        }
        else
        {
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
                Status = _assessmentRun.Status
            };

            try
            {
                AssessmentsService.UpdateAssessmentRun(assessRun);

                var result = new AssessmentRunDialogResult()
                {
                    Action = ResultActions.Ok
                };

                Close(result);
            }
            catch(Exception e)
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

    public override async Task ActivateAsync(AssessmentRunDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        await Dispatcher.UIThread.Invoke( async () =>
        {
            if (parameter.Operation == OperationType.Edit)
            {
                StrTitle = StrEditAssessmentRun;

                _assessmentRun = parameter.AssessmentRun;

                Comments = _assessmentRun!.Comments;
            }
            else
            {
                StrTitle = StrNewAssessmentRun;
            }

            _operation = parameter.Operation;
            
            Entities = new ObservableCollection<Entity>(await EntitiesService.GetAllAsync(null,true));

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

        }, DispatcherPriority.Background,  cancellationToken);
        
        //return Task.Run(() => { }, cancellationToken);
    }
    
    
    
    #endregion
}