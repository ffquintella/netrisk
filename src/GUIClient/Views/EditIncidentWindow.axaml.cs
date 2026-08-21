using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

public partial class EditIncidentWindow : DialogWindowBase<IncidentDialogResult>
{
    public EditIncidentWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires the assignee autocomplete to the view-model's populator. This lives here rather than
    /// in the view-model, which used to reach into the window to find the control (IX-9).
    /// </summary>
    protected override void OnOpened()
    {
        base.OnOpened();

        if (DataContext is not EditIncidentViewModel viewModel) return;

        var userListingBox = this.FindControl<AutoCompleteBox>("UserListingBox");
        if (userListingBox == null) return;

        userListingBox.AsyncPopulator = viewModel.GetUserByNameAsync;
        userListingBox.TextSelector = viewModel.TextSelector;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
