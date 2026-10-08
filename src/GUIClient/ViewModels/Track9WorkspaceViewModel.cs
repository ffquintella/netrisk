using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using GUIClient.Tools.Track9;
using GUIClient.ViewModels.Track9;
using ReactiveUI;

namespace GUIClient.ViewModels;

/// <summary>Permission-filtered, lazy desktop entry points to MIGR-TI/IA governance.</summary>
public sealed class Track9WorkspaceViewModel : ViewModelBase
{
    public string StrTitle => Localizer["Track9Workspace"];
    public ObservableCollection<Track9Section> Sections { get; } = [];
    private Track9Section? _selectedSection;
    public Track9Section? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (ReferenceEquals(value, _selectedSection)) return;
            this.RaiseAndSetIfChanged(ref _selectedSection, value);
            _ = OpenAsync(value);
        }
    }
    private ViewModelBase? _content;
    public ViewModelBase? Content
    {
        get => _content;
        private set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    public Track9WorkspaceViewModel()
    {
        var user = AuthenticationService.AuthenticatedUserInfo;
        if (Track9WorkspaceAccess.CanReadRisk(user))
        {
            Add("Track9TopRisks", () => new TopRisksViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9FlaggedRegister", () => new RiskFlagFilterViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9TreatmentPortfolio", () => new TreatmentPortfolioViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9TailPortfolio", () => new TailPortfolioViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9Correlations", () => new CorrelationEditorViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9Kris", () => new KriRegisterViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9Reassessments", () => new ReassessmentQueueViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9Metrics", () => new MethodologyMetricsViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9Archives", () => new ArchiveReviewViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9Backtesting", () => new BacktestingViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9Committees", () => new RiskCommitteesViewModel(), vm => vm.EnsureLoadedAsync());
        }
        if (Track9WorkspaceAccess.CanReadRegister(user, "third_party_manage"))
        {
            Add("Track9ThirdParties", () => new ThirdPartyRegisterViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9Concentration", () => new ThirdPartyConcentrationViewModel(), vm => vm.EnsureLoadedAsync());
        }
        if (Track9WorkspaceAccess.CanReadRegister(user, "data_catalogue_manage"))
        {
            Add("Track9DataCatalogue", () => new DataCatalogueViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9LegalRequirements", () => new LegalRequirementsViewModel(), vm => vm.EnsureLoadedAsync());
            Add("Track9Dpia", () => new DpiaViewModel(), vm => vm.EnsureLoadedAsync());
        }
        if (Track9WorkspaceAccess.CanReadRegister(user, "ai_governance_manage"))
            Add("Track9AiModels", () => new AiModelInventoryViewModel(), vm => vm.EnsureLoadedAsync());
        if (Sections.Count > 0) SelectedSection = Sections[0];
    }

    private void Add<T>(string label, Func<T> factory, Func<T, Task> load) where T : ViewModelBase =>
        Sections.Add(new Track9Section(Localizer[label], () => factory(), vm => load((T)vm)));

    private async Task OpenAsync(Track9Section? section)
    {
        Content = section?.GetViewModel();
        if (section is null) return;
        // Each section owns its loading and failure state. Catch unexpected initialization failures here,
        // since selecting a navigation item must not escape through a fire-and-forget continuation.
        await RunAsync(string.Empty, () => section.LoadAsync());
    }

    public override void Dispose()
    {
        foreach (var section in Sections) section.Dispose();
        base.Dispose();
    }
}

public sealed class Track9Section(string title, Func<ViewModelBase> factory, Func<ViewModelBase, Task> load)
    : IDisposable
{
    private ViewModelBase? _viewModel;
    public string Title { get; } = title;
    public ViewModelBase GetViewModel() => _viewModel ??= factory();
    public Task LoadAsync() => load(GetViewModel());
    public void Dispose() => _viewModel?.Dispose();
}
