using System;
using DAL.Enums;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>The object the detail opens on (S40 §5.9.2): its kind and reference, on the node and snapshot day shown.</summary>
public class RiskIndexObjectDetailDialogParameter : NavigationParameterBase
{
    public RiskIndexObjectKind Kind { get; set; }
    public string Reference { get; set; } = "";
    public string? Node { get; set; }
    public DateOnly? Date { get; set; }
}
