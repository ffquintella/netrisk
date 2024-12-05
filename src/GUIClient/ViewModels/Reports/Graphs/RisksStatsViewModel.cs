using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.Statistics;
using ReactiveUI;

namespace GUIClient.ViewModels.Reports.Graphs;

public class RisksStatsViewModel: ViewModelBase
{
    #region FIELDS
    
    private bool _initialized = false;
    
    #endregion

    #region PROPERTIES

    private RisksNumbers _risksNumbers = new RisksNumbers();
    public RisksNumbers RisksNumbers
    {
        get => _risksNumbers;
        set => this.RaiseAndSetIfChanged(ref _risksNumbers, value);
    }
    
    private RisksNumbersViewModel _risksNumbersViewModel = new RisksNumbersViewModel();
    public RisksNumbersViewModel RisksNumbersViewModel
    {
        get => _risksNumbersViewModel;
        set => this.RaiseAndSetIfChanged(ref _risksNumbersViewModel, value);
    }
    
    private RisksGroupsViewModel _risksGroupsViewModel = new RisksGroupsViewModel();
    public RisksGroupsViewModel RisksGroupsViewModel
    {
        get => _risksGroupsViewModel;
        set => this.RaiseAndSetIfChanged(ref _risksGroupsViewModel, value);
    }
    
    private BusinessProcessRisksViewModel _businessProcessRisksViewModel = new BusinessProcessRisksViewModel();
    public BusinessProcessRisksViewModel BusinessProcessRisksViewModel
    {
        get => _businessProcessRisksViewModel;
        set => this.RaiseAndSetIfChanged(ref _businessProcessRisksViewModel, value);
    }

    #endregion
    
    #region SERVICES

    private readonly IStatisticsService _statisticsService = GetService<IStatisticsService>();
    
    #endregion
    
    #region METHODS
    public async Task InitializeAsync()
    {
        if(!_initialized)
        {
            RisksNumbers = await _statisticsService.GetRisksNumbersAsync();
            RisksNumbersViewModel.Initialize(RisksNumbers);
            RisksGroupsViewModel.Initialize(await _statisticsService.GetRisksTopGroupsAsync());
            BusinessProcessRisksViewModel.Initialize(await _statisticsService.GetRisksTopEntitiesAsync(10, "businessProcess"));
            _initialized = true;
        }
    }
    #endregion
}