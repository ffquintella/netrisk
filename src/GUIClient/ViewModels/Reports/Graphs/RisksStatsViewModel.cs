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

    #endregion
    
    #region SERVICES

    private readonly IStatisticsService _statisticsService = GetService<IStatisticsService>();
    
    #endregion
    
    #region METHODS
    public async void InitializeAsync()
    {
        if(!_initialized)
        {
            RisksNumbers = await _statisticsService.GetRisksNumbersAsync();
            RisksNumbersViewModel.Initialize(RisksNumbers);
            _initialized = true;
        }
    }
    #endregion
}