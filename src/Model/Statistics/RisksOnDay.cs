using System;

namespace Model.Statistics;

public class RisksOnDay
{
    public DateTime Day { get; set; }
    public int RisksCreated { get; set; }
    public float TotalRiskValue { get; set; }
}