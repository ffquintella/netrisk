using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DAL.Entities;
using GUIClient.ViewModels.Assessments;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views.Assessments;

public partial class AssessmentRunDialog : DialogWindowBase<AssessmentRunDialogResult>
{
    public AssessmentRunDialog()
    {
        InitializeComponent();
    }

    private void SelectingItemsControl_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is null) return;
        if (DataContext is null) return;
        ((AssessmentRunDialogViewModel)DataContext).ProcessSelectionChange(((ComboBox)sender).SelectedItem as AssessmentAnswer);
    }
}