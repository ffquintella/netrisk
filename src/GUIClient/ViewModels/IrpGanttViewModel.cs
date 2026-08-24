using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using ClientServices.Interfaces;
using Model.Exceptions;
using Model.IncidentResponsePlan;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>
/// One bar on the Gantt. The geometry is computed here rather than in a converter because a bar
/// needs the whole timeline to place itself, and a converter only ever sees one value.
/// </summary>
public class GanttRow
{
    public IrpScheduleItem Item { get; init; } = null!;

    public string Name => Item.Name;

    public string Window => $"{Item.StartDate:g} → {Item.EndDate:g}";

    /// <summary>Left offset of the bar inside the timeline, as a margin.</summary>
    public Thickness BarMargin { get; init; }

    public double BarWidth { get; init; }

    public bool IsCritical => Item.IsCritical;

    public bool IsOverdue => Item.IsOverdue;

    public bool IsBlocked => Item.IsBlocked;

    /// <summary>Slack rendered for the row; empty string on the critical path.</summary>
    public string SlackText { get; init; } = string.Empty;
}

/// <summary>
/// Task-dependency Gantt with critical-path highlighting for one incident response plan
/// (Track 2 milestone 2.4.3).
///
/// The schedule — early/late start, slack, critical flag — is computed server-side; this
/// view-model only turns the returned offsets into pixels.
/// </summary>
public class IrpGanttViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle => Localizer["Response Timeline"];
    public string StrTask => Localizer["Task"];
    public string StrTimeline => Localizer["Timeline"];
    public string StrRefresh => Localizer["Refresh"];
    public string StrCriticalPath => Localizer["Critical path"];
    public string StrOverdue => Localizer["Overdue"];
    public string StrBlocked => Localizer["Blocked"];
    public string StrSlack => Localizer["Slack"];
    public string StrNoTasks => Localizer["This plan has no tasks to schedule"];
    public string StrTotalDuration => Localizer["Total duration"];
    public string StrDependencies => Localizer["Dependencies"];
    public string StrDependsOn => Localizer["Depends On"];
    public string StrAddDependency => Localizer["Add dependency"];
    public string StrRemoveDependency => Localizer["Remove dependency"];
    public string StrTaskWaits => Localizer["Task"];
    public string StrCompleteWithOverride => Localizer["Complete anyway"];
    public string StrOverrideReason => Localizer["Override reason"];
    public string StrOverridePrompt => Localizer["This task is blocked. State why it is being completed anyway."];

    #endregion

    #region PROPERTIES

    private IIncidentResponsePlansService PlansService { get; } = GetService<IIncidentResponsePlansService>();

    private readonly int _planId;

    /// <summary>
    /// Width the timeline is drawn into. Fixed so every bar shares one scale; the window scrolls
    /// horizontally rather than rescaling, which keeps bar lengths comparable between refreshes.
    /// </summary>
    public double TimelineWidth => 760;

    private string _planName = string.Empty;
    public string PlanName
    {
        get => _planName;
        set => this.RaiseAndSetIfChanged(ref _planName, value);
    }

    private ObservableCollection<GanttRow> _rows = new();
    public ObservableCollection<GanttRow> Rows
    {
        get => _rows;
        set => this.RaiseAndSetIfChanged(ref _rows, value);
    }

    private string? _summary;
    public string? Summary
    {
        get => _summary;
        set => this.RaiseAndSetIfChanged(ref _summary, value);
    }

    /// <summary>Left offset of the "now" marker, or null when now falls outside the plan window.</summary>
    private Thickness? _todayMargin;
    public Thickness? TodayMargin
    {
        get => _todayMargin;
        set
        {
            this.RaiseAndSetIfChanged(ref _todayMargin, value);
            this.RaisePropertyChanged(nameof(HasToday));
        }
    }

    public bool HasToday => TodayMargin != null;

    private ObservableCollection<IrpTaskDependency> _dependencies = new();
    public ObservableCollection<IrpTaskDependency> Dependencies
    {
        get => _dependencies;
        set => this.RaiseAndSetIfChanged(ref _dependencies, value);
    }

    private IrpTaskDependency? _selectedDependency;
    public IrpTaskDependency? SelectedDependency
    {
        get => _selectedDependency;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDependency, value);
            this.RaisePropertyChanged(nameof(HasSelectedDependency));
        }
    }

    public bool HasSelectedDependency => SelectedDependency != null;

    private GanttRow? _selectedRow;
    public GanttRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedRow, value);
            this.RaisePropertyChanged(nameof(HasSelectedRow));
            this.RaisePropertyChanged(nameof(SelectedRowIsBlocked));
            this.RaisePropertyChanged(nameof(PredecessorCandidates));
        }
    }

    public bool HasSelectedRow => SelectedRow != null;

    /// <summary>Gates the override action: only a blocked task needs one.</summary>
    public bool SelectedRowIsBlocked => SelectedRow?.IsBlocked == true;

    private GanttRow? _newPredecessor;
    public GanttRow? NewPredecessor
    {
        get => _newPredecessor;
        set => this.RaiseAndSetIfChanged(ref _newPredecessor, value);
    }

    /// <summary>Every other task on the plan. The server still refuses one that closes a cycle.</summary>
    public ObservableCollection<GanttRow> PredecessorCandidates =>
        new(Rows.Where(r => SelectedRow == null || r.Item.TaskId != SelectedRow.Item.TaskId));

    private string _overrideReason = string.Empty;
    public string OverrideReason
    {
        get => _overrideReason;
        set => this.RaiseAndSetIfChanged(ref _overrideReason, value);
    }

    private bool _hasLoaded;
    public bool HasLoaded
    {
        get => _hasLoaded;
        set => this.RaiseAndSetIfChanged(ref _hasLoaded, value);
    }

    public bool IsEmpty => HasLoaded && Rows.Count == 0;

    #endregion

    public ReactiveCommand<RxVoid, RxVoid> RefreshCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddDependencyCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveDependencyCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CompleteWithOverrideCommand { get; }

    public IrpGanttViewModel(int planId, string planName)
    {
        _planId = planId;
        PlanName = planName;
        RefreshCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        AddDependencyCommand = ReactiveCommand.CreateFromTask(AddDependencyAsync);
        RemoveDependencyCommand = ReactiveCommand.CreateFromTask(RemoveDependencyAsync);
        CompleteWithOverrideCommand = ReactiveCommand.CreateFromTask(CompleteWithOverrideAsync);
    }

    /// <summary>Parameterless overload so the XAML designer can instantiate the view.</summary>
    public IrpGanttViewModel() : this(0, string.Empty)
    {
    }

    public async Task InitializeAsync()
    {
        if (_planId <= 0) return;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await WithBusyAsync(async () =>
        {
            try
            {
                var schedule = await PlansService.GetScheduleAsync(_planId);
                Build(schedule);

                Dependencies = new ObservableCollection<IrpTaskDependency>(
                    await PlansService.GetDependenciesAsync(_planId));
            }
            catch (Exception ex)
            {
                Logger.Error("Error loading the schedule of plan {PlanId}: {Message}", _planId, ex.Message);
                Toasts.Error(Localizer["Could not load the response timeline"]);
                Rows = new ObservableCollection<GanttRow>();
            }
            finally
            {
                HasLoaded = true;
                this.RaisePropertyChanged(nameof(IsEmpty));
            }
        });
    }

    private void Build(IrpSchedule schedule)
    {
        PlanName = schedule.PlanName;

        var total = schedule.TotalDuration.TotalSeconds;

        // A plan whose tasks all have zero duration would divide by zero; fall back to a scale
        // where every bar is a visible sliver rather than drawing nothing at all.
        var scale = total > 0 ? TimelineWidth / total : 0;

        var rows = new List<GanttRow>();

        foreach (var item in schedule.Items.OrderBy(i => i.EarlyStart).ThenBy(i => i.ExecutionOrder))
        {
            var left = item.EarlyStart.TotalSeconds * scale;
            var width = Math.Max(item.Duration.TotalSeconds * scale, MinimumBarWidth);

            // Keep the bar inside the timeline even after the minimum-width bump.
            if (left + width > TimelineWidth) left = Math.Max(0, TimelineWidth - width);

            rows.Add(new GanttRow
            {
                Item = item,
                BarMargin = new Thickness(left, 0, 0, 0),
                BarWidth = width,
                SlackText = item.IsCritical ? string.Empty : FormatDuration(item.Slack)
            });
        }

        Rows = new ObservableCollection<GanttRow>(rows);

        Summary = $"{StrTotalDuration}: {FormatDuration(schedule.TotalDuration)} · " +
                  $"{StrCriticalPath}: {schedule.CriticalPath.Count}";

        TodayMargin = ComputeTodayMargin(schedule, scale);
    }

    private Thickness? ComputeTodayMargin(IrpSchedule schedule, double scale)
    {
        var now = DateTime.UtcNow;
        if (now < schedule.PlanStart || now > schedule.PlanEnd) return null;

        var offset = (now - schedule.PlanStart).TotalSeconds * scale;
        return new Thickness(Math.Clamp(offset, 0, TimelineWidth), 0, 0, 0);
    }

    /// <summary>A bar narrower than this is invisible, so short tasks are floored to it.</summary>
    private const double MinimumBarWidth = 4;

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return "0h";
        if (duration.TotalDays >= 1) return $"{duration.TotalDays:0.#}d";
        if (duration.TotalHours >= 1) return $"{duration.TotalHours:0.#}h";
        return $"{duration.TotalMinutes:0}m";
    }

    private async Task AddDependencyAsync()
    {
        if (SelectedRow == null || NewPredecessor == null)
        {
            Toasts.Warning(Localizer["Pick a task and the task it waits on"]);
            return;
        }

        await WithBusyAsync(async () =>
        {
            try
            {
                await PlansService.AddDependencyAsync(
                    _planId, SelectedRow.Item.TaskId, NewPredecessor.Item.TaskId);

                await LoadAsync();
                Toasts.Success(Localizer["Dependency added."]);
            }
            catch (RuleBrokenException ex)
            {
                // A cycle is the author's mistake and the server's message names the two tasks,
                // so show it rather than a generic failure.
                Toasts.Warning(ex.Message);
            }
            catch (Exception ex)
            {
                Logger.Error("Error adding dependency: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not add the dependency"]);
            }
        });
    }

    private async Task RemoveDependencyAsync()
    {
        if (SelectedDependency == null) return;

        await WithBusyAsync(async () =>
        {
            try
            {
                await PlansService.RemoveDependencyAsync(
                    _planId, SelectedDependency.TaskId, SelectedDependency.DependsOnTaskId);

                await LoadAsync();
                Toasts.Success(Localizer["Dependency removed."]);
            }
            catch (Exception ex)
            {
                Logger.Error("Error removing dependency: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not remove the dependency"]);
            }
        });
    }

    private async Task CompleteWithOverrideAsync()
    {
        if (SelectedRow == null) return;

        if (string.IsNullOrWhiteSpace(OverrideReason))
        {
            Toasts.Warning(Localizer["An override reason is required"]);
            return;
        }

        await WithBusyAsync(async () =>
        {
            try
            {
                await PlansService.CompleteBlockedTaskAsync(_planId, SelectedRow.Item.TaskId, OverrideReason);

                OverrideReason = string.Empty;
                await LoadAsync();
                Toasts.Success(Localizer["Task completed with a recorded override."]);
            }
            catch (RuleBrokenException ex)
            {
                Toasts.Warning(ex.Message);
            }
            catch (Exception ex)
            {
                Logger.Error("Error completing blocked task: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not complete the task"]);
            }
        });
    }
}
