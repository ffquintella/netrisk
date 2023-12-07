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

namespace GUIClient.ViewModels.Assessments;

public class AssessmentRunDialogViewModel: ParameterizedDialogViewModelBaseAsync<AssessmentRunDialogResult, AssessmentRunDialogParameter>
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
    
    #endregion
    
    #region SERVICES
        private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();
        private IAssessmentsService AssessmentsService { get; } = GetService<IAssessmentsService>();
        
    #endregion
    
    #region FIELDS
        private Assessment? _assessment;
    #endregion
    
    
    #region METHODS
    
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