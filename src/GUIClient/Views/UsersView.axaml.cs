﻿using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaExtraControls.Models;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        DataContext = new ViewModels.UsersViewModel();
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void MSTeams_OnSelectedItemsChanged(object? sender, SelectedItemsChangedEventHandlerArgs e)
    {
        ((UsersViewModel) DataContext).SelectedTeamUsers = new ObservableCollection<SelectEntity>(e.SelectedItems);
    }
}