using Model.Statistics;

namespace ServerServices.Interfaces;

public interface IStatisticsService
{
    /// <summary>
    /// Gets the points representing the risk score on one axes and the cost to mitigate on the other.
    /// </summary>
    /// <returns></returns>
    public List<LabeledPoints> GetRisksVsCosts(double minRisk, double maxRisk);
}