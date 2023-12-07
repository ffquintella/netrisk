using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views.Assessments;

public partial class AssessmentRunDialog : DialogWindowBase<AssessmentRunDialogResult>
{
    public AssessmentRunDialog()
    {
        InitializeComponent();
    }
}