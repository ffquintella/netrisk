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
        LockSize();
        CenterDialog();

        OnOpened();
    }

    private void CenterDialog()
    {
        var x = ParentWindow.Position.X + (ParentWindow.Bounds.Width - Width) / 2;
        var y = ParentWindow.Position.Y + (ParentWindow.Bounds.Height - Height) / 2;

        Position = new PixelPoint((int) x, (int) y);
    }

    private void LockSize()
    {
        MaxWidth = MinWidth = Width;
        MaxHeight = MinHeight = Height;
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