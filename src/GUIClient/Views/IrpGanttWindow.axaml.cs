using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;

namespace GUIClient.Views;

/// <summary>
/// Task-dependency Gantt for one incident response plan (Track 2 milestone 2.4.3). An auxiliary
/// window rather than another panel on the plan editor, which is already dense.
/// </summary>
public partial class IrpGanttWindow : AuxiliaryWindowBase
{
    public IrpGanttWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is IrpGanttViewModel vm)
            {
                _ = vm.InitializeAsync();
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
