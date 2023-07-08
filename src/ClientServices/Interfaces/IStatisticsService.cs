using Model.DTO.Statistics;
using Model.Statistics;

namespace ClientServices.Interfaces;

public interface IStatisticsService
{
    List<RisksOnDay> GetRisksOverTime();
    SecurityControlsStatistics GetSecurityControlStatistics();

}