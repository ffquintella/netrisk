using Avalonia.Controls;

namespace GUIClient.Views.Admin;

/// <summary>
/// Code-behind for the Jira Service Management and Assets tabs (Track 4 milestone 4.6). Empty by
/// design: every interaction is a bound command on
/// <see cref="ViewModels.Admin.JiraIntegrationViewModel"/>, per the interaction standard.
/// </summary>
public partial class JiraIntegrationView : UserControl
{
    public JiraIntegrationView()
    {
        InitializeComponent();
    }
}
