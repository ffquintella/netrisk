using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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


    private void SelectingItemsControl_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is null) return;
        if (DataContext is null) return;
        
        var answer = ((AssessmentRunDialogViewModel)DataContext).LoadQuestionAnswer( (((ComboBox)sender).Items[0] as AssessmentAnswer).QuestionId);
        
        if(answer == null) return;
        
        var idx = ((ComboBox)sender).ItemsSource.Cast<AssessmentAnswer>().ToList().FindIndex(x => x.Id == answer.Id);
        ((ComboBox)sender).SelectedIndex = idx;
        
        //((ComboBox)sender).SelectedItem = answer;
    }
}