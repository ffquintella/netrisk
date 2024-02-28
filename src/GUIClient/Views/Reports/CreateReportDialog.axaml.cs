using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views.Reports;

public partial class CreateReportDialog : DialogWindowBase<ReportDialogResult>
{
    public CreateReportDialog()
    {
        InitializeComponent();
    }
}