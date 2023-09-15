using System.Drawing;
using Material.Icons;

namespace GUIClient.Models;

public class ReportType
{
    public string Name { get; set; } = string.Empty;
    public int Id { get; set; } = 0;
    public MaterialIconKind Icon { get; set; } = MaterialIconKind.TimerSandEmpty;

    public ReportType(int id, string name, MaterialIconKind icon = MaterialIconKind.TimerSandEmpty)
    {
        Id = id;
        Name = name;
        Icon = icon;
    }

}