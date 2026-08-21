using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Validation;
using Model.Incidents;
using Model.Status;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Admin;

/// <summary>
/// IRP template editor and automation-rule configuration (Track 2 milestone 2.4.1 / 2.4.2).
///
/// A template is a playbook: an ordered task list plus a matching rule that decides which
/// incidents activate it. Both halves are edited here rather than as raw JSON — the automation
/// engine reads <c>MatchingRulesJson</c> and <c>AssigneeRuleJson</c>, so this screen composes
/// and parses those documents on the author's behalf.
/// </summary>
public class IrpTemplatesViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle => Localizer["IRP Templates"];
    public string StrTemplates => Localizer["Templates"];
    public string StrTasks => Localizer["Tasks"];
    public string StrName => Localizer["Name"];
    public string StrDescription => Localizer["Description"];
    public string StrEnabled => Localizer["Enabled"];
    public string StrMatchingRules => Localizer["Automation Rules"];
    public string StrCategory => Localizer["Category"];
    public string StrStatus => Localizer["Status"];
    public string StrAnyCategory => Localizer["Any category"];
    public string StrAnyStatus => Localizer["Any status"];
    public string StrAdd => Localizer["Add"];
    public string StrEdit => Localizer["Edit"];
    public string StrDelete => Localizer["Delete"];
    public string StrClone => Localizer["Clone"];
    public string StrRefresh => Localizer["Refresh"];
    public string StrTaskTitle => Localizer["Title"];
    public string StrInstructions => Localizer["Instructions"];
    public string StrAssignee => Localizer["Assignee"];
    public string StrAssigneeType => Localizer["Assignee Type"];
    public string StrAssigneeValue => Localizer["Assignee Id"];
    public string StrDueOffset => Localizer["Due Offset (hours)"];
    public string StrPredecessor => Localizer["Depends On"];
    public string StrNoPredecessor => Localizer["No dependency"];
    public string StrRequiresConfirmation => Localizer["Requires coordinator approval"];
    public string StrNoTemplateSelected => Localizer["Select a template to edit"];
    public string StrTemplateSavedMSG => Localizer["Template saved."];
    public string StrTemplateDeletedMSG => Localizer["Template deleted."];
    public string StrTaskSavedMSG => Localizer["Task saved."];
    public string StrTaskDeletedMSG => Localizer["Task deleted."];

    #endregion

    #region PROPERTIES

    private IIrpTemplatesService TemplatesService { get; } = GetService<IIrpTemplatesService>();

    public List<IncidentCategory> Categories { get; }
    public List<IntStatusItem> StatusItems { get; }

    /// <summary>Assignee resolution modes understood by <c>IrpAutomationService</c>.</summary>
    public List<string> AssigneeTypes { get; } = ["User", "Role"];

    private ObservableCollection<IrpTemplate> _templates = new();
    public ObservableCollection<IrpTemplate> Templates
    {
        get => _templates;
        set => this.RaiseAndSetIfChanged(ref _templates, value);
    }

    private IrpTemplate? _selectedTemplate;
    public IrpTemplate? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTemplate, value);
            this.RaisePropertyChanged(nameof(HasSelectedTemplate));
            LoadSelectedTemplate();
        }
    }

    public bool HasSelectedTemplate => SelectedTemplate != null;

    private ObservableCollection<IrpTemplateTask> _tasks = new();
    public ObservableCollection<IrpTemplateTask> Tasks
    {
        get => _tasks;
        set => this.RaiseAndSetIfChanged(ref _tasks, value);
    }

    private IrpTemplateTask? _selectedTask;
    public IrpTemplateTask? SelectedTask
    {
        get => _selectedTask;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTask, value);
            this.RaisePropertyChanged(nameof(HasSelectedTask));
            LoadSelectedTask();
        }
    }

    public bool HasSelectedTask => SelectedTask != null;

    #endregion

    #region EDITED TEMPLATE FIELDS

    private string _templateName = string.Empty;
    public string TemplateName
    {
        get => _templateName;
        set => this.RaiseAndSetIfChanged(ref _templateName, value);
    }

    private string? _templateDescription;
    public string? TemplateDescription
    {
        get => _templateDescription;
        set => this.RaiseAndSetIfChanged(ref _templateDescription, value);
    }

    private bool _templateEnabled;
    public bool TemplateEnabled
    {
        get => _templateEnabled;
        set => this.RaiseAndSetIfChanged(ref _templateEnabled, value);
    }

    /// <summary>Null means "match any category".</summary>
    private IncidentCategory? _matchCategory;
    public IncidentCategory? MatchCategory
    {
        get => _matchCategory;
        set => this.RaiseAndSetIfChanged(ref _matchCategory, value);
    }

    /// <summary>Null means "match any status".</summary>
    private IntStatusItem? _matchStatus;
    public IntStatusItem? MatchStatus
    {
        get => _matchStatus;
        set => this.RaiseAndSetIfChanged(ref _matchStatus, value);
    }

    #endregion

    #region EDITED TASK FIELDS

    private string _taskTitle = string.Empty;
    public string TaskTitle
    {
        get => _taskTitle;
        set => this.RaiseAndSetIfChanged(ref _taskTitle, value);
    }

    private string? _taskInstructions;
    public string? TaskInstructions
    {
        get => _taskInstructions;
        set => this.RaiseAndSetIfChanged(ref _taskInstructions, value);
    }

    private string _taskAssigneeType = "User";
    public string TaskAssigneeType
    {
        get => _taskAssigneeType;
        set => this.RaiseAndSetIfChanged(ref _taskAssigneeType, value);
    }

    private string _taskAssigneeValue = "1";
    public string TaskAssigneeValue
    {
        get => _taskAssigneeValue;
        set => this.RaiseAndSetIfChanged(ref _taskAssigneeValue, value);
    }

    /// <summary>Authored in hours; the wire format is seconds from plan activation.</summary>
    private double _taskDueOffsetHours;
    public double TaskDueOffsetHours
    {
        get => _taskDueOffsetHours;
        set => this.RaiseAndSetIfChanged(ref _taskDueOffsetHours, value);
    }

    private IrpTemplateTask? _taskPredecessor;
    public IrpTemplateTask? TaskPredecessor
    {
        get => _taskPredecessor;
        set => this.RaiseAndSetIfChanged(ref _taskPredecessor, value);
    }

    private bool _taskRequiresConfirmation;
    public bool TaskRequiresConfirmation
    {
        get => _taskRequiresConfirmation;
        set => this.RaiseAndSetIfChanged(ref _taskRequiresConfirmation, value);
    }

    /// <summary>
    /// Candidate predecessors: every other task on the template. The server still rejects an
    /// edge that closes a cycle, but excluding the task itself avoids the obvious mistake here.
    /// </summary>
    public ObservableCollection<IrpTemplateTask> PredecessorCandidates
    {
        get
        {
            var candidates = Tasks.Where(t => SelectedTask == null || t.Id != SelectedTask.Id);
            return new ObservableCollection<IrpTemplateTask>(candidates);
        }
    }

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> RefreshCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddTemplateCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveTemplateCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteTemplateCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloneTemplateCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddTaskCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveTaskCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteTaskCommand { get; }

    #endregion

    public IrpTemplatesViewModel()
    {
        Categories = IncidentCategories.GetCategories(Localizer);
        StatusItems = IncidentStatus.GetAll(Localizer);

        this.ValidationRule(
            vm => vm.TemplateName,
            name => !string.IsNullOrWhiteSpace(name),
            Localizer["The template name is required"]);

        this.ValidationRule(
            vm => vm.TaskTitle,
            title => !string.IsNullOrWhiteSpace(title) || SelectedTask == null,
            Localizer["The task title is required"]);

        RefreshCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        AddTemplateCommand = ReactiveCommand.CreateFromTask(AddTemplateAsync);
        SaveTemplateCommand = ReactiveCommand.CreateFromTask(SaveTemplateAsync);
        DeleteTemplateCommand = ReactiveCommand.CreateFromTask(DeleteTemplateAsync);
        CloneTemplateCommand = ReactiveCommand.CreateFromTask(CloneTemplateAsync);
        AddTaskCommand = ReactiveCommand.CreateFromTask(AddTaskAsync);
        SaveTaskCommand = ReactiveCommand.CreateFromTask(SaveTaskAsync);
        DeleteTaskCommand = ReactiveCommand.CreateFromTask(DeleteTaskAsync);
    }

    public async Task InitializeAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await WithBusyAsync(async () =>
        {
            try
            {
                var templates = await TemplatesService.GetAllAsync();
                var previousId = SelectedTemplate?.Id;

                Templates = new ObservableCollection<IrpTemplate>(templates.OrderBy(t => t.Name));
                SelectedTemplate = Templates.FirstOrDefault(t => t.Id == previousId) ?? Templates.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Logger.Error("Error loading IRP templates: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not load the IRP templates"]);
            }
        });
    }

    private void LoadSelectedTemplate()
    {
        if (SelectedTemplate == null)
        {
            TemplateName = string.Empty;
            TemplateDescription = null;
            TemplateEnabled = false;
            MatchCategory = null;
            MatchStatus = null;
            Tasks = new ObservableCollection<IrpTemplateTask>();
            SelectedTask = null;
            return;
        }

        TemplateName = SelectedTemplate.Name;
        TemplateDescription = SelectedTemplate.Description;
        TemplateEnabled = SelectedTemplate.IsEnabled;

        ApplyMatchingRules(SelectedTemplate.MatchingRulesJson);

        _ = LoadTasksAsync(SelectedTemplate.Id);
    }

    /// <summary>
    /// Parses the stored rule document into the two pickers. A rule the engine cannot read is
    /// treated as "match anything" rather than throwing the editor open in a broken state.
    /// </summary>
    private void ApplyMatchingRules(string? json)
    {
        MatchCategory = null;
        MatchStatus = null;

        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var rules = JsonSerializer.Deserialize<MatchingRules>(json, JsonOptions);
            if (rules == null) return;

            if (!string.IsNullOrEmpty(rules.Category))
            {
                MatchCategory = Categories.FirstOrDefault(c => c.DbName == rules.Category);
            }

            if (rules.Status.HasValue)
            {
                MatchStatus = StatusItems.FirstOrDefault(s => s.IntStatus == rules.Status.Value);
            }
        }
        catch (JsonException ex)
        {
            Logger.Warning("IRP template {Id} has an unreadable matching rule: {Message}",
                SelectedTemplate?.Id, ex.Message);
        }
    }

    private string ComposeMatchingRules()
    {
        var rules = new MatchingRules
        {
            Category = MatchCategory?.DbName,
            Status = MatchStatus?.IntStatus
        };

        return JsonSerializer.Serialize(rules, JsonOptions);
    }

    private async Task LoadTasksAsync(int templateId)
    {
        try
        {
            var tasks = await TemplatesService.GetTasksAsync(templateId);
            Tasks = new ObservableCollection<IrpTemplateTask>(tasks);
            SelectedTask = null;
            this.RaisePropertyChanged(nameof(PredecessorCandidates));
        }
        catch (Exception ex)
        {
            Logger.Error("Error loading tasks of IRP template {Id}: {Message}", templateId, ex.Message);
            Toasts.Error(Localizer["Could not load the template tasks"]);
        }
    }

    private void LoadSelectedTask()
    {
        this.RaisePropertyChanged(nameof(PredecessorCandidates));

        if (SelectedTask == null)
        {
            TaskTitle = string.Empty;
            TaskInstructions = null;
            TaskAssigneeType = "User";
            TaskAssigneeValue = "1";
            TaskDueOffsetHours = 0;
            TaskPredecessor = null;
            TaskRequiresConfirmation = false;
            return;
        }

        TaskTitle = SelectedTask.Title;
        TaskInstructions = SelectedTask.InstructionsMarkdown;
        TaskDueOffsetHours = Math.Round(SelectedTask.DueOffsetSeconds / 3600.0, 2);
        TaskRequiresConfirmation = SelectedTask.RequiresConfirmation;
        TaskPredecessor = SelectedTask.PredecessorTaskId.HasValue
            ? Tasks.FirstOrDefault(t => t.Id == SelectedTask.PredecessorTaskId.Value)
            : null;

        ApplyAssigneeRule(SelectedTask.AssigneeRuleJson);
    }

    private void ApplyAssigneeRule(string? json)
    {
        TaskAssigneeType = "User";
        TaskAssigneeValue = "1";

        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var rule = JsonSerializer.Deserialize<AssigneeRule>(json, JsonOptions);
            if (rule == null) return;

            TaskAssigneeType = AssigneeTypes.Contains(rule.Type) ? rule.Type : "User";
            TaskAssigneeValue = rule.Value;
        }
        catch (JsonException ex)
        {
            Logger.Warning("IRP template task {Id} has an unreadable assignee rule: {Message}",
                SelectedTask?.Id, ex.Message);
        }
    }

    private string ComposeAssigneeRule() =>
        JsonSerializer.Serialize(new AssigneeRule { Type = TaskAssigneeType, Value = TaskAssigneeValue }, JsonOptions);

    private async Task AddTemplateAsync()
    {
        await WithBusyAsync(async () =>
        {
            try
            {
                var created = await TemplatesService.CreateAsync(new IrpTemplate
                {
                    Name = Localizer["New template"],
                    Description = null,
                    // A new template starts disabled so it cannot match incidents before it has tasks.
                    IsEnabled = false,
                    MatchingRulesJson = JsonSerializer.Serialize(new MatchingRules(), JsonOptions)
                });

                Templates.Add(created);
                SelectedTemplate = created;
                Toasts.Success(StrTemplateSavedMSG);
            }
            catch (Exception ex)
            {
                Logger.Error("Error creating IRP template: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not create the template"]);
            }
        });
    }

    private async Task SaveTemplateAsync()
    {
        if (SelectedTemplate == null) return;

        if (string.IsNullOrWhiteSpace(TemplateName))
        {
            Toasts.Warning(Localizer["The template name is required"]);
            return;
        }

        await WithBusyAsync(async () =>
        {
            try
            {
                SelectedTemplate.Name = TemplateName;
                SelectedTemplate.Description = TemplateDescription;
                SelectedTemplate.IsEnabled = TemplateEnabled;
                SelectedTemplate.MatchingRulesJson = ComposeMatchingRules();

                await TemplatesService.UpdateAsync(SelectedTemplate);

                // Re-sort: the name may have moved the row in the list.
                var id = SelectedTemplate.Id;
                Templates = new ObservableCollection<IrpTemplate>(Templates.OrderBy(t => t.Name));
                _selectedTemplate = Templates.FirstOrDefault(t => t.Id == id);
                this.RaisePropertyChanged(nameof(SelectedTemplate));

                Toasts.Success(StrTemplateSavedMSG);
            }
            catch (Exception ex)
            {
                Logger.Error("Error saving IRP template: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not save the template"]);
            }
        });
    }

    private async Task DeleteTemplateAsync()
    {
        if (SelectedTemplate == null) return;

        await WithBusyAsync(async () =>
        {
            try
            {
                await TemplatesService.DeleteAsync(SelectedTemplate.Id);
                Templates.Remove(SelectedTemplate);
                SelectedTemplate = Templates.FirstOrDefault();
                Toasts.Success(StrTemplateDeletedMSG);
            }
            catch (Exception ex)
            {
                Logger.Error("Error deleting IRP template: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not delete the template"]);
            }
        });
    }

    private async Task CloneTemplateAsync()
    {
        if (SelectedTemplate == null) return;

        await WithBusyAsync(async () =>
        {
            try
            {
                var clone = await TemplatesService.CloneAsync(SelectedTemplate.Id);
                Templates.Add(clone);
                Templates = new ObservableCollection<IrpTemplate>(Templates.OrderBy(t => t.Name));
                SelectedTemplate = Templates.FirstOrDefault(t => t.Id == clone.Id);
                Toasts.Success(StrTemplateSavedMSG);
            }
            catch (Exception ex)
            {
                Logger.Error("Error cloning IRP template: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not clone the template"]);
            }
        });
    }

    private async Task AddTaskAsync()
    {
        if (SelectedTemplate == null) return;

        await WithBusyAsync(async () =>
        {
            try
            {
                var created = await TemplatesService.CreateTaskAsync(SelectedTemplate.Id, new IrpTemplateTask
                {
                    Title = Localizer["New task"],
                    AssigneeRuleJson = JsonSerializer.Serialize(new AssigneeRule(), JsonOptions),
                    DueOffsetSeconds = 0,
                    RequiresConfirmation = false
                });

                Tasks.Add(created);
                SelectedTask = created;
                Toasts.Success(StrTaskSavedMSG);
            }
            catch (Exception ex)
            {
                Logger.Error("Error creating IRP template task: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not create the task"]);
            }
        });
    }

    private async Task SaveTaskAsync()
    {
        if (SelectedTemplate == null || SelectedTask == null) return;

        if (string.IsNullOrWhiteSpace(TaskTitle))
        {
            Toasts.Warning(Localizer["The task title is required"]);
            return;
        }

        await WithBusyAsync(async () =>
        {
            try
            {
                SelectedTask.Title = TaskTitle;
                SelectedTask.InstructionsMarkdown = TaskInstructions;
                SelectedTask.AssigneeRuleJson = ComposeAssigneeRule();
                SelectedTask.DueOffsetSeconds = (int)Math.Round(TaskDueOffsetHours * 3600);
                SelectedTask.PredecessorTaskId = TaskPredecessor?.Id;
                SelectedTask.RequiresConfirmation = TaskRequiresConfirmation;

                await TemplatesService.UpdateTaskAsync(SelectedTemplate.Id, SelectedTask);

                // Reload so a server-rejected dependency does not linger in the local copy.
                await LoadTasksAsync(SelectedTemplate.Id);
                Toasts.Success(StrTaskSavedMSG);
            }
            catch (Exception ex)
            {
                Logger.Error("Error saving IRP template task: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not save the task"]);
                await LoadTasksAsync(SelectedTemplate.Id);
            }
        });
    }

    private async Task DeleteTaskAsync()
    {
        if (SelectedTemplate == null || SelectedTask == null) return;

        await WithBusyAsync(async () =>
        {
            try
            {
                await TemplatesService.DeleteTaskAsync(SelectedTemplate.Id, SelectedTask.Id);
                await LoadTasksAsync(SelectedTemplate.Id);
                Toasts.Success(StrTaskDeletedMSG);
            }
            catch (Exception ex)
            {
                Logger.Error("Error deleting IRP template task: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not delete the task"]);
            }
        });
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Mirrors <c>ServerServices.Services.IrpMatchingRules</c>. Duplicated rather than shared
    /// because GUIClient does not reference ServerServices.
    /// </summary>
    private sealed class MatchingRules
    {
        public string? Category { get; set; }
        public int? Status { get; set; }
    }

    /// <summary>Mirrors <c>ServerServices.Services.IrpAssigneeRule</c>.</summary>
    private sealed class AssigneeRule
    {
        public string Type { get; set; } = "User";
        public string Value { get; set; } = "1";
    }
}
