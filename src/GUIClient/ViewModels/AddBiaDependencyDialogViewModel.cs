using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Continuity;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>A process or service the selected node can depend on.</summary>
public sealed class ContinuityProviderOption(int entityId, string name)
{
    public int EntityId { get; } = entityId;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

/// <summary>
/// Declares that the selected process or service depends on another (Stage 9.3, S43 §7). The picker
/// lists every other process and service not already a provider; a cycle is allowed — it is reported in
/// the panel, not refused — and the server refuses a self-dependency and a duplicate anyway.
/// </summary>
public class AddBiaDependencyDialogViewModel
    : ParameterizedDialogViewModelBaseAsync<ContinuityDialogResult, ContinuityDialogParameter>
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["AddDependencyTitle"];
    public string StrProvider { get; } = Localizer["DependencyProvider"];
    public string StrDescription { get; } = Localizer["DependencyDescription"];
    public string StrAdd { get; } = Localizer["AddDependency"];

    #endregion

    private IContinuityService ContinuityService { get; } = GetService<IContinuityService>();

    #region PROPERTIES

    private int _entityId;

    private string _entityName = string.Empty;
    public string EntityName { get => _entityName; set => this.RaiseAndSetIfChanged(ref _entityName, value); }

    public ObservableCollection<ContinuityProviderOption> Providers { get; } = [];

    private ContinuityProviderOption? _selectedProvider;
    public ContinuityProviderOption? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            // The ComboBox writes null when it cannot resolve the value; that never clears a choice.
            if (value is null && _selectedProvider is not null && Providers.Contains(_selectedProvider)) return;
            this.RaiseAndSetIfChanged(ref _selectedProvider, value);
            this.RaisePropertyChanged(nameof(IsAddEnabled));
        }
    }

    private string? _description;
    public string? Description { get => _description; set => this.RaiseAndSetIfChanged(ref _description, value); }

    public bool IsAddEnabled => SelectedProvider is not null;

    #endregion

    public ReactiveCommand<RxVoid, RxVoid> BtAddClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

    public AddBiaDependencyDialogViewModel()
    {
        BtAddClicked = ReactiveCommand.CreateFromTask(AddAsync);
        BtCancelClicked = ReactiveCommand.Create(() => Close(new ContinuityDialogResult
            { Action = ResultActions.Cancel, Changed = false }));
    }

    public override async Task ActivateAsync(ContinuityDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        _entityId = parameter.EntityId;
        EntityName = parameter.EntityName;

        try
        {
            await WithBusyAsync(async () =>
            {
                var subjects = await ContinuityService.GetSubjectsAsync();

                Providers.Clear();
                foreach (var subject in subjects
                             .Where(s => s.EntityId != _entityId && !parameter.ExistingProviderIds.Contains(s.EntityId))
                             .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
                    Providers.Add(new ContinuityProviderOption(subject.EntityId,
                        $"{subject.Name} ({Localizer[subject.DefinitionName]})"));
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error loading the continuity subjects");
            Toasts.Error(ExplainError(ex));
        }
    }

    private async Task AddAsync()
    {
        if (SelectedProvider is null) return;

        var request = new BiaDependencyCreateRequest { ProviderEntityId = SelectedProvider.EntityId, Description = Description };

        var added = false;
        await RunAsync(string.Empty, async () =>
        {
            await ContinuityService.AddDependencyAsync(_entityId, request);
            added = true;
        });

        if (added) Close(new ContinuityDialogResult { Action = ResultActions.Ok, Changed = true });
    }
}
