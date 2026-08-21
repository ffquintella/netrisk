using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GUIClient.Interfaces;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.ViewModels.Dialogs;

public class DialogWindowBase<TResult> : Window
    where TResult : DialogResultBase
{
    private Window ParentWindow => (Window) Owner!;

    protected DialogViewModelBase<TResult> ViewModel => (DialogViewModelBase<TResult>) DataContext!;

    protected DialogWindowBase()
    {
        SubscribeToViewEvents();
    }

    /// <summary>
    /// Centralised keyboard accessibility for every modal dialog:
    /// <c>Esc</c> dismisses the dialog, and <c>Ctrl/Cmd+S</c> commits it when the
    /// view-model opts in via <see cref="ISaveableDialog"/>.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            DataContext is DialogViewModelBase<TResult> vm && vm.CloseCommand.CanExecute(null))
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
            return;
        }

        var saveChord = e.Key == Key.S &&
                        (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
        if (saveChord && DataContext is ISaveableDialog saveable &&
            saveable.SaveCommand is { } save && save.CanExecute(null))
        {
            save.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected virtual void OnOpened()
    {

    }

    private void OnOpened(object sender, EventArgs e)
    {
        ApplySizeContract();
        CenterDialog();

        OnOpened();
    }

    private void CenterDialog()
    {
        var width = double.IsNaN(Width) ? Bounds.Width : Width;
        var height = double.IsNaN(Height) ? Bounds.Height : Height;

        var x = ParentWindow.Position.X + (ParentWindow.Bounds.Width - width) / 2;
        var y = ParentWindow.Position.Y + (ParentWindow.Bounds.Height - height) / 2;

        Position = new PixelPoint((int) x, (int) y);
    }

    /// <summary>
    /// Applies the sizing contract of IX-1: the size declared in XAML is authoritative.
    /// Fixed dialogs (<c>CanResize="False"</c>) are pinned to it; resizable dialogs and
    /// wizards (<c>CanResize="True"</c>) keep it only as a floor, so a declared
    /// <c>CanResize</c> is honoured at runtime instead of being silently neutered.
    /// </summary>
    private void ApplySizeContract()
    {
        // When a dialog uses SizeToContent, Width/Height are NaN until the window
        // is laid out; fall back to the realised Bounds so we never assign NaN.
        var width = double.IsNaN(Width) ? Bounds.Width : Width;
        var height = double.IsNaN(Height) ? Bounds.Height : Height;

        if (CanResize)
        {
            // A Min already declared in XAML wins; otherwise the opening size is the floor.
            if (double.IsNaN(MinWidth) || MinWidth <= 0) MinWidth = width;
            if (double.IsNaN(MinHeight) || MinHeight <= 0) MinHeight = height;
            return;
        }

        MaxWidth = MinWidth = width;
        MaxHeight = MinHeight = height;
    }

    private void SubscribeToViewModelEvents() => ViewModel.CloseRequested += ViewModelOnCloseRequested!;

    private void UnsubscribeFromViewModelEvents() => ViewModel.CloseRequested -= ViewModelOnCloseRequested!;

    private void SubscribeToViewEvents()
    {
        DataContextChanged += OnDataContextChanged!;
        Opened += OnOpened!;
    }

    private void UnsubscribeFromViewEvents()
    {
        DataContextChanged -= OnDataContextChanged!;
        Opened -= OnOpened!;
    }

    private void OnDataContextChanged(object sender, EventArgs e) => SubscribeToViewModelEvents();

    private void ViewModelOnCloseRequested(object sender, DialogResultEventArgs<TResult> args)
    {
        UnsubscribeFromViewModelEvents();
        UnsubscribeFromViewEvents();

        Close(args.Result);
    }
}

public class DialogWindowBase : DialogWindowBase<DialogResultBase>
{

}