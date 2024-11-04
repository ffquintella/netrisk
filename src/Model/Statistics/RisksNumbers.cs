using Model.Statistics.Risks;

namespace Model.Statistics;

public class RisksNumbers
{

    public GeneralNumbers General { get; set; } = new GeneralNumbers();
    public ByStatus ByStatus { get; set; } = new ByStatus();
    
}