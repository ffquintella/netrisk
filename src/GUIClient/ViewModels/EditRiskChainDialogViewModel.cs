using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using GUIClient.Tools;
using GUIClient.Tools.Hosts;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Risks.Chain;
using ReactiveUI;
using Tools.Risks;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>A level of the chain as the level picker shows it.</summary>
public sealed class ChainLevelOption(RiskChainLevel level, string name)
{
    public RiskChainLevel Level { get; } = level;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

/// <summary>Something a risk can be linked to: an entity of a chain type, or a host.</summary>
public sealed class ChainTargetOption(int? entityId, int? hostId, string name)
{
    public int? EntityId { get; } = entityId;
    public int? HostId { get; } = hostId;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

/// <summary>
/// Edits one risk's linkage chain (Stage 9.1, S41 §7): the links per level, a level and target picker
/// to add one, and — at the Asset level — a host search.
///
/// Every operation is saved immediately; there is no batch to commit or discard (S41 §11, D12). The
/// server decides what is allowed and this view-model only mirrors it to enable buttons: host search
/// needs <c>hosts</c>, a Legacy link is removed from the risk's Entity field rather than from here,
/// and a refusal shows the server's own sentence.
/// </summary>
public class EditRiskChainDialogViewModel
    : ParameterizedDialogViewModelBaseAsync<EditRiskChainDialogResult, EditRiskChainDialogParameter>
{
    private const int HostSearchPageSize = 25;

    #region LANGUAGE

    public string StrTitle { get; } = Localizer["EditChain"];
    public string StrLinkageChain { get; } = Localizer["LinkageChain"];
    public string StrChainLevel { get; } = Localizer["ChainLevel"];
    public string StrChainTarget { get; } = Localizer["ChainTarget"];
    public string StrChainOrigin { get; } = Localizer["ChainOrigin"];
    public string StrChainOriginDeclared { get; } = Localizer["ChainOriginDeclared"];
    public string StrChainOriginLegacy { get; } = Localizer["ChainOriginLegacy"];
    public string StrChainLinkedAt { get; } = Localizer["ChainLinkedAt"];
    public string StrAddChainLink { get; } = Localizer["AddChainLink"];
    public string StrRemoveChainLink { get; } = Localizer["RemoveChainLink"];
    public string StrSearchHost { get; } = Localizer["SearchHost"];
    public string StrSelectChainTarget { get; } = Localizer["SelectChainTarget"];
    public string StrChainLegacyLinkHint { get; } = Localizer["ChainLegacyLinkHint"];
    public string StrChainDemotedHint { get; } = Localizer["ChainDemotedHint"];
    public string StrChainHostRedacted { get; } = Localizer["ChainHostRedacted"];
    public string StrRequiresHostsPermission { get; } = Localizer["RequiresHostsPermission"];
    public string StrSearch { get; } = Localizer["Search"];

    #endregion

    #region SERVICES

    private IRiskChainService ChainService { get; } = GetService<IRiskChainService>();
    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();
    private IHostsService HostsService { get; } = GetService<IHostsService>();

    #endregion

    #region PROPERTIES

    private bool _changed;
    private List<Entity> _entities = [];

    private int _riskId;
    public int RiskId
    {
        get => _riskId;
        set => this.RaiseAndSetIfChanged(ref _riskId, value);
    }

    private string _riskSubject = string.Empty;
    public string RiskSubject
    {
        get => _riskSubject;
        set => this.RaiseAndSetIfChanged(ref _riskSubject, value);
    }

    public ObservableCollection<RiskChainLinkRow> Links { get; } = [];

    private RiskChainLinkRow? _selectedLink;
    public RiskChainLinkRow? SelectedLink
    {
        get => _selectedLink;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLink, value);
            this.RaisePropertyChanged(nameof(IsRemoveLinkEnabled));
            this.RaisePropertyChanged(nameof(IsLegacyHintVisible));
        }
    }

    public List<ChainLevelOption> Levels { get; }

    private ChainLevelOption? _selectedLevel;
    public ChainLevelOption? SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLevel, value);
            RefreshTargets();
            this.RaisePropertyChanged(nameof(IsHostSearchVisible));
            this.RaisePropertyChanged(nameof(IsSearchHostEnabled));
            this.RaisePropertyChanged(nameof(IsHostsPermissionHintVisible));
            this.RaisePropertyChanged(nameof(IsAddLinkEnabled));
        }
    }

    public ObservableCollection<ChainTargetOption> Targets { get; } = [];

    private ChainTargetOption? _selectedTarget;
    public ChainTargetOption? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTarget, value);
            this.RaisePropertyChanged(nameof(IsAddLinkEnabled));
        }
    }

    private string _hostSearchText = string.Empty;
    public string HostSearchText
    {
        get => _hostSearchText;
        set => this.RaiseAndSetIfChanged(ref _hostSearchText, value);
    }

    /// <summary>Whether the signed-in user may see and link hosts — the <c>hosts</c> permission or admin.</summary>
    public bool CanReadHosts { get; }

    public bool IsHostSearchVisible => SelectedLevel?.Level == RiskChainLevel.Asset;

    /// <summary>
    /// Shown instead of a silently greyed-out search: without <c>hosts</c> the host search is
    /// unavailable, and applications stay selectable at the Asset level.
    /// </summary>
    public bool IsHostsPermissionHintVisible => IsHostSearchVisible && !CanReadHosts;

    public bool IsSearchHostEnabled => IsHostSearchVisible && CanReadHosts;

    public bool IsAddLinkEnabled => SelectedLevel is not null && SelectedTarget is not null;

    public bool IsRemoveLinkEnabled => RiskChainAccess.CanRemove(SelectedLink?.Link, CanReadHosts);

    public bool IsLegacyHintVisible => SelectedLink?.IsLegacy == true;

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtAddLinkClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveLinkClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSearchHostClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCloseClicked { get; }

    #endregion

    public EditRiskChainDialogViewModel()
    {
        CanReadHosts = RiskChainAccess.CanReadHosts(AuthenticationService.AuthenticatedUserInfo);

        Levels = Enum.GetValues<RiskChainLevel>()
            .OrderBy(l => (int)l)
            .Select(l => new ChainLevelOption(l, LevelName(l)))
            .ToList();

        BtAddLinkClicked = ReactiveCommand.CreateFromTask(AddLinkAsync);
        BtRemoveLinkClicked = ReactiveCommand.CreateFromTask(RemoveLinkAsync);
        BtSearchHostClicked = ReactiveCommand.CreateFromTask(SearchHostsAsync);
        BtCloseClicked = ReactiveCommand.Create(() => Close(new EditRiskChainDialogResult
        {
            Action = _changed ? ResultActions.Ok : ResultActions.Cancel,
            Changed = _changed
        }));
    }

    /// <summary>The level's name as the risk detail shows it. Shared so the two screens agree.</summary>
    public static string LevelName(RiskChainLevel level) => level switch
    {
        RiskChainLevel.Objective => Localizer["ChainLevelObjective"],
        RiskChainLevel.Process => Localizer["ChainLevelProcess"],
        RiskChainLevel.ItService => Localizer["ChainLevelItService"],
        RiskChainLevel.Data => Localizer["ChainLevelData"],
        _ => Localizer["ChainLevelAsset"]
    };

    public override async Task ActivateAsync(EditRiskChainDialogParameter parameter,
        CancellationToken cancellationToken = default)
    {
        RiskId = parameter.RiskId;
        RiskSubject = parameter.RiskSubject;

        try
        {
            await WithBusyAsync(async () =>
            {
                _entities = await EntitiesService.GetAllAsync(null, true);
                await ReloadLinksAsync();
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error loading the chain of risk {Id}", RiskId);
            Toasts.Error(ExplainError(ex));
        }

        SelectedLevel = Levels.FirstOrDefault();
    }

    private async Task ReloadLinksAsync()
    {
        var chain = await ChainService.GetRiskChainAsync(RiskId);

        Links.Clear();
        foreach (var row in RiskChainLinkRow.From(chain, LevelName, StrChainHostRedacted,
                     StrChainOriginDeclared, StrChainOriginLegacy))
            Links.Add(row);

        SelectedLink = null;
    }

    /// <summary>The entities of the chosen level's types. Host results, if any, are added by the search.</summary>
    private void RefreshTargets()
    {
        Targets.Clear();
        SelectedTarget = null;

        if (SelectedLevel is null) return;

        foreach (var entity in _entities
                     .Where(e => RiskChainSchema.LevelOf(e.DefinitionName) == SelectedLevel.Level)
                     .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Targets.Add(new ChainTargetOption(entity.Id, null,
                $"{entity.DisplayName} ({Localizer[entity.DefinitionName]})"));
        }
    }

    private async Task SearchHostsAsync()
    {
        if (!IsSearchHostEnabled) return;

        try
        {
            await WithBusyAsync(async () =>
            {
                var filter = HostsFilterComposer.Compose(HostSearchText, null, null, null, null);
                var hosts = await HostsService.GetFilteredAsync(HostSearchPageSize, 1, filter);

                // Earlier host results go; the level's entities stay.
                foreach (var stale in Targets.Where(t => t.HostId is not null).ToList()) Targets.Remove(stale);

                foreach (var host in hosts)
                {
                    var name = new[] { host.HostName, host.Fqdn, host.Ip }
                        .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "#" + host.Id;
                    Targets.Add(new ChainTargetOption(null, host.Id,
                        string.IsNullOrWhiteSpace(host.Ip) || name == host.Ip ? name : $"{name} [{host.Ip}]"));
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Host search failed");
            Toasts.Error(ExplainError(ex));
        }
    }

    private async Task AddLinkAsync()
    {
        if (SelectedTarget is null) return;

        var request = new RiskChainLinkCreateDto
        {
            EntityId = SelectedTarget.EntityId,
            HostId = SelectedTarget.HostId
        };

        await RunAsync(string.Empty, async () =>
        {
            await ChainService.AddLinkAsync(RiskId, request);
            _changed = true;
            await ReloadLinksAsync();
            SelectedTarget = null;
        });
    }

    private async Task RemoveLinkAsync()
    {
        if (!IsRemoveLinkEnabled || SelectedLink is null) return;

        var linkId = SelectedLink.Link.Id;

        await RunAsync(string.Empty, async () =>
        {
            var demoted = await ChainService.DeleteLinkAsync(RiskId, linkId);
            _changed = true;

            // Still on the risk's Entity field, so the server kept it as a Legacy link rather than
            // deleting it. Say so, or the row that did not disappear reads as a failure.
            if (demoted is not null) Toasts.Info(StrChainDemotedHint);

            await ReloadLinksAsync();
        });
    }
}
