using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClientServices.Interfaces;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;

namespace GUIClient.Tools;

/// <summary>
/// Shared helper that prompts the user for a destination and writes exported
/// report bytes to it using the Avalonia 11 <see cref="IStorageProvider"/> API.
/// </summary>
public static class ExportFileSaver
{
    /// <summary>
    /// Shows a modal dialog asking the user which format to export to.
    /// Returns the chosen <see cref="ExportFormat"/>, or <c>null</c> if the user cancelled.
    /// </summary>
    public static async Task<ExportFormat?> PickFormatAsync(Avalonia.Controls.Window? owner, string title, string message, bool includePdf = true)
    {
        var buttons = new List<ButtonDefinition>();
        if (includePdf) buttons.Add(new ButtonDefinition { Name = "PDF" });
        buttons.Add(new ButtonDefinition { Name = "CSV" });
        buttons.Add(new ButtonDefinition { Name = "Excel" });
        buttons.Add(new ButtonDefinition { Name = "Cancel", IsCancel = true });

        var box = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
        {
            ContentTitle = title,
            ContentMessage = message,
            Icon = Icon.Info,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ButtonDefinitions = buttons
        });

        var result = owner is null ? await box.ShowAsync() : await box.ShowWindowDialogAsync(owner);

        return result switch
        {
            "PDF" => ExportFormat.Pdf,
            "CSV" => ExportFormat.Csv,
            "Excel" => ExportFormat.Xlsx,
            _ => null
        };
    }

    public static async Task SaveAsync(Avalonia.Controls.Window? owner, ExportFormat format, byte[] data)
    {
        var topLevel = owner is null ? null : TopLevel.GetTopLevel(owner);
        if (topLevel is null) return;

        var extension = format.ToString().ToLowerInvariant();

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export",
            DefaultExtension = extension,
            SuggestedFileName = "export." + extension,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(format.ToString())
                {
                    Patterns = new[] { "*." + extension }
                }
            }
        });

        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(data.AsMemory(0, data.Length));
    }
}
