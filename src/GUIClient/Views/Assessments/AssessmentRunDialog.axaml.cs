using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DAL.Entities;
using GUIClient.ViewModels.Assessments;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using MsBox.Avalonia;
using Serilog;

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

        AssessmentRunDialogViewModel dc = (AssessmentRunDialogViewModel)DataContext;

        if (dc == null)
        {
            Log.Error("AssessmentRunDialogViewModel is null");
            throw new System.Exception("DataContext is null in assessment run dialog");
        }

        if (((ComboBox)sender).Items.Count == 0)
        {
            Log.Warning("ComboBox has no items in assessment run dialog");
            return;
        }
        
        AssessmentAnswer? aAnswer = (((ComboBox)sender).Items[0] as AssessmentAnswer)!;
        
        if (aAnswer == null)
        {
            
            Log.Warning("AssessmentAnswer is null in assessment run dialog");
            return;
        }
        
        var answer = dc.LoadQuestionAnswer(aAnswer.QuestionId);
        
        if(answer == null) return;
        
        var idx = ((ComboBox)sender).ItemsSource!.Cast<AssessmentAnswer>().ToList().FindIndex(x => x.Id == answer.Id);
        ((ComboBox)sender).SelectedIndex = idx;
        
        //((ComboBox)sender).SelectedItem = answer;
    }
}