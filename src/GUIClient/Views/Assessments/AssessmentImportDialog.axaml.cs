using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GUIClient.ViewModels.Assessments;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views.Assessments;

public partial class AssessmentImportDialog : DialogWindowBase<AssessmentImportResult>
{
    public AssessmentImportDialog()
    {
        InitializeComponent();

        var browse = this.FindControl<Button>("BtBrowse");
        if (browse is not null) browse.Click += OnBrowseClicked;
    }

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AssessmentImportDialogViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Assessment template",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Assessment templates") { Patterns = new[] { "*.json", "*.xlsx" } },
                new("JSON") { Patterns = new[] { "*.json" } },
                new("Excel") { Patterns = new[] { "*.xlsx" } }
            }
        });

        if (files.Count == 0) return;
        vm.SelectedFilePath = files[0].Path.LocalPath;
    }
}
