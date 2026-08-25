using Avalonia.Controls;

namespace GUIClient.Views.Admin;

/// <summary>
/// Code-behind for the Track 4 integrations administration screen. Empty by design: every interaction
/// is a bound command on <see cref="ViewModels.Admin.IntegrationsViewModel"/>, per the interaction
/// standard.
/// </summary>
public partial class IntegrationsView : UserControl
{
    public IntegrationsView()
    {
        InitializeComponent();
    }
}
