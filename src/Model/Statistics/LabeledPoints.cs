using LiveChartsCore.Defaults;

namespace Model.Statistics;

public class LabeledPoints: ObservablePoint
{
    //public double X { get; set; } = 0;
    //public float Y { get; set; } = 0;
    public string Label { get; set; } = "";
}