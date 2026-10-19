using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using Model.RiskIndex;
using ReactiveUI;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>
/// The asset-class tabs (S40 §5.5; T253): Dispositivos, Internet-facing, Identidades, Aplicações and Nuvem, each the node
/// shown ∩ its class — a tab never changes the node of the page. A node already cut by a class enables only that tab. The
/// selected tab loads first, then the other headers' levels one after another, so the module never holds more than the two
/// derived computations per user the server admits (§8.1).
/// </summary>
public class RiskAssetClassTabsViewModel : ViewModelBase
{
    public string StrTitle { get; } = Localizer["RiskTabsTitle"];
    public string StrNotComparable { get; } = Localizer["RiskTabsNotComparableNotice"];
    public string StrFactorObjectsHint { get; } = Localizer["RiskFactorsOpenObjectsHint"];

    private CancellationTokenSource? _loading;
    private readonly Action<RiskIndexTabGroup>? _tabSelected;
    private readonly Action<RiskIndexTabGroup, string, string?>? _openFactorObjects;

    /// <summary>
    /// <paramref name="tabSelected"/> reports a selection to the module, which keeps it in its <c>DrillState</c> (§5.6.2);
    /// <paramref name="openFactorObjects"/> is a factor line's "›" (T254).
    /// </summary>
    public RiskAssetClassTabsViewModel(Action<RiskIndexTabGroup>? tabSelected = null,
        Action<RiskIndexTabGroup, string, string?>? openFactorObjects = null)
    {
        _tabSelected = tabSelected;
        _openFactorObjects = openFactorObjects;
    }

    public ObservableCollection<RiskAssetClassTabViewModel> Tabs { get; } = [];

    private RiskAssetClassTabViewModel? _selectedTab;
    public RiskAssetClassTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (value is { IsEnabled: false } || value == _selectedTab) return;
            this.RaiseAndSetIfChanged(ref _selectedTab, value);
            if (value is null) return;
            _tabSelected?.Invoke(value.Tab);
            _ = value.LoadContentAsync();
        }
    }

    private bool _isActive;
    public bool IsActive { get => _isActive; private set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    public void Clear()
    {
        _loading?.Cancel();
        foreach (var tab in Tabs) tab.Dispose();
        Tabs.Clear();
        _selectedTab = null;
        this.RaisePropertyChanged(nameof(SelectedTab));
        IsActive = false;
    }

    /// <summary>
    /// The node shown changed: rebuild the five tabs for it and read the selected one — the drill state's tab when it is
    /// enabled (§5.6.2) — then the other levels. Reads queue on <paramref name="gate"/>, the page's, so the module never
    /// holds more than the two derived computations per user the server admits (§8.1).
    /// </summary>
    public async Task LoadAsync(string node, DateOnly date, RiskIndexOverviewDto overview, RiskIndexTabGroup? preferred = null,
        SemaphoreSlim? gate = null)
    {
        var previous = preferred ?? _selectedTab?.Tab;
        Clear();
        var loading = _loading = new CancellationTokenSource();
        gate ??= new SemaphoreSlim(1, 1);
        var culture = CultureInfo.CurrentCulture;

        foreach (var group in AssetTabFormatter.Tabs)
        {
            var tab = new RiskAssetClassTabViewModel(group,
                AssetTabFormatter.DisabledReason(node, group, RiskHeadlineViewModel.Text, culture), _openFactorObjects);
            tab.SetContext(node, date, overview.Bands, loading.Token, gate);
            Tabs.Add(tab);
        }

        IsActive = true;
        var selected = Tabs.FirstOrDefault(t => t.Tab == previous && t.IsEnabled) ?? Tabs.FirstOrDefault(t => t.IsEnabled);
        _selectedTab = selected;
        this.RaisePropertyChanged(nameof(SelectedTab));
        if (selected is null) return;
        if (selected.Tab != preferred) _tabSelected?.Invoke(selected.Tab);

        await selected.LoadLevelAsync();
        if (loading.IsCancellationRequested) return;
        await selected.LoadContentAsync();
        foreach (var tab in Tabs.Where(t => t != selected && t.IsEnabled))
        {
            if (loading.IsCancellationRequested) return;
            await tab.LoadLevelAsync();
        }
    }

    public override void Dispose()
    {
        Clear();
        base.Dispose();
    }
}
