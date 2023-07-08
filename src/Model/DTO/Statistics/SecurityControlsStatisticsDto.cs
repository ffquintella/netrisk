using System.Collections.Generic;

namespace Model.DTO.Statistics;

public class SecurityControlsStatistics
{
    public List<SecurityControlStatistic> SecurityControls { get; set; } = new List<SecurityControlStatistic>();
    public List<FrameworkStatistic>? FameworkStats { get; set; }

}