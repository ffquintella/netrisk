using System.Threading.Tasks;
using Avalonia.Controls;
using GUIClient.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;

namespace GUIClient.Tools;

/// <summary>
/// The single confirmation pattern for destructive and irreversible actions, per
/// <c>docs/ux-interaction-standard.md</c> IX-4: always Yes/No, always with the item's
/// name interpolated, and with the consequence spelled out when there is one
/// (cascade to children, loss of a record, …).
///
/// Every delete/irreversible action goes through here instead of hand-rolling a
/// <see cref="MessageBoxManager"/> call with its own button set — that is exactly how the
/// app ended up with four different confirmation button sets for the same job.
/// </summary>
public static class ConfirmationDialog
{
    /// <summary>
    /// Asks the user to confirm deleting <paramref name="itemName"/>.
    /// </summary>
    /// <param name="itemName">Name of the item being deleted; interpolated into the prompt.</param>
    /// <param name="consequence">
    /// Optional extra sentence describing what else the deletion takes with it
    /// (already-localized text, e.g. a cascade warning).
    /// </param>
    /// <returns><c>true</c> when the user chose Yes.</returns>
    public static Task<bool> ConfirmDeleteAsync(string? itemName, string? consequence = null)
    {
        var message = string.Format(ViewModelBase.Localizer["ConfirmDeleteMSG"], itemName ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(consequence))
        {
            message += "\n\n" + consequence;
        }

        return ConfirmAsync(ViewModelBase.Localizer["Warning"], message);
    }

    /// <summary>
    /// Asks the user to confirm an irreversible non-delete action with an
    /// already-composed, already-localized message.
    /// </summary>
    /// <returns><c>true</c> when the user chose Yes.</returns>
    public static async Task<bool> ConfirmAsync(string title, string message)
    {
        var result = await MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = title,
                ContentMessage = message,
                Icon = Icon.Question,
                ButtonDefinitions = ButtonEnum.YesNo,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            })
            .ShowAsync();

        return result == ButtonResult.Yes;
    }
}
