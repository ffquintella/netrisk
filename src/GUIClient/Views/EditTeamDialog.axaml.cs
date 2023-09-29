﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

public partial class EditTeamDialog : DialogWindowBase<StringDialogResult>
{
    public EditTeamDialog()
    {
        InitializeComponent();
    }
}