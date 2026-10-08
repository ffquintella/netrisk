using System;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.ThirdParties;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

/// <summary>Stage 9.10 concentration by supplier, cloud and identity provider.</summary>
public class ThirdPartyConcentrationViewModel : ViewModelBase
{
    private readonly IThirdPartiesService _service;
    private readonly GUIClient.Tools.Track9.Track9RegistersLoadGate _loadGate = new();
    private bool _loaded;

    public string StrTitle { get; } = Localizer["Track9ThirdPartyConcentration"];
    public string StrRefresh { get; } = Localizer["Refresh"];
    public string StrName { get; } = Localizer["Name"];
    public string StrSupplier { get; } = Localizer["Track9Supplier"];
    public string StrCloud { get; } = Localizer["Track9Cloud"];
    public string StrIdentity { get; } = Localizer["Track9Identity"];
    public string StrCriticalProcesses { get; } = Localizer["Track9CriticalProcesses"];
    public string StrShare { get; } = Localizer["Track9Share"];
    public string StrFourthParty { get; } = Localizer["Track9FourthParty"];
    public string StrUnmapped { get; } = Localizer["Track9UnmappedDependencies"];
    public string StrRestricted { get; } = Localizer["Track9ScopeRestricted"];
    public string StrLoadError { get; } = Localizer["Track9LoadError"];

    private ThirdPartyConcentrationReportDto? _report;
    public ThirdPartyConcentrationReportDto? Report
    {
        get => _report;
        private set
        {
            this.RaiseAndSetIfChanged(ref _report, value);
            this.RaisePropertyChanged(nameof(HasReport));
        }
    }
    public bool HasReport => Report is not null;

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }

    public ThirdPartyConcentrationViewModel() : this(GetService<IThirdPartiesService>()) { }
    internal ThirdPartyConcentrationViewModel(IThirdPartiesService service)
    {
        _service = service;
        ReloadCommand = ReactiveCommand.CreateFromTask(ReloadAsync);
    }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded) await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        var generation = _loadGate.Begin();
        await WithBusyAsync(async () =>
        {
            try
            {
                var report = await _service.GetConcentrationAsync();
                if (!_loadGate.IsCurrent(generation)) return;
                Report = report;
                ErrorMessage = null;
                _loaded = true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to load third-party concentration");
                if (!_loadGate.IsCurrent(generation)) return;
                ErrorMessage = StrLoadError;
            }
        });
    }
}
