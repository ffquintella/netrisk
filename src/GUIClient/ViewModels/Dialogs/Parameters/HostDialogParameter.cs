using System;
using DAL.Entities;
using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

public class HostDialogParameter: NavigationParameterBase
{
    public OperationType Operation { get; set; }
    public Host? Host { get; set; }

    /// <summary>When the screen read <see cref="Host"/> from the server (review M63 #7); null means "now".</summary>
    public DateTimeOffset? LoadedAt { get; set; }
}