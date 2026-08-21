using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;

namespace GUIClient.Views;

/// <summary>
/// First-run server-URL prompt. Closes with the accepted URL as its result, or with
/// <c>null</c> when dismissed — Esc and the Cancel button both dismiss (IX-8).
/// </summary>
public partial class LoadConfigurationWindow : Window
{
    public LoadConfigurationWindow()
    {
        var viewModel = new LoadConfigurationViewModel();
        viewModel.Completed += OnCompleted;

        DataContext = viewModel;
        InitializeComponent();

        Opened += (_, _) => this.FindControl<TextBox>("TxtServerUrl")?.Focus();
    }

    /// <summary>The accepted server URL, or an empty string when the user dismissed the window.</summary>
    public string ServerUrl { get; private set; } = string.Empty;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnCompleted(object? sender, string? url)
    {
        ServerUrl = url ?? string.Empty;
        Close();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
