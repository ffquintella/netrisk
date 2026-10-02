using System;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GUIClient.ViewModels;
using ReactiveUI;

namespace GUIClient.Views;

public partial class HostsView : UserControl
{
    public HostsView()
    {
        var viewModel = new HostsViewModel();
        DataContext = viewModel;
        InitializeComponent();
        BindLeftPaneWidth(viewModel);

        // Posted, so it runs after the TabControl has finished applying its own initial selection.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(viewModel.OnViewReady);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Keeps the host list's column and <see cref="HostsViewModel.LeftPaneWidth"/> in step
    /// (S38 §3.1, <c>hostsView.leftPaneWidth</c>).
    ///
    /// This is the one piece of layout that needs code-behind: a <see cref="ColumnDefinition"/> is not
    /// a control and has no DataContext, so its Width cannot carry a binding. The stored width is
    /// pushed into the column, and the column's own Width changes — which is what the splitter
    /// writes, by mouse or by arrow key — are reported back, debounced so a drag is one save rather
    /// than one per pixel.
    /// </summary>
    private void BindLeftPaneWidth(HostsViewModel viewModel)
    {
        var layout = this.FindControl<Grid>("HostsLayout");
        if (layout == null || layout.ColumnDefinitions.Count == 0) return;

        var listColumn = layout.ColumnDefinitions[0];

        viewModel.WhenAnyValue(vm => vm.LeftPaneWidth)
            .Subscribe(width =>
            {
                if (!listColumn.Width.IsAbsolute || Math.Abs(listColumn.Width.Value - width) >= 1)
                    listColumn.Width = new GridLength(width);
            });

        listColumn.GetObservable(ColumnDefinition.WidthProperty)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(400))
            .Subscribe(width => Dispatcher.UIThread.Post(() =>
            {
                // A star width would be a proportion, not pixels; ActualWidth is what the user sees.
                viewModel.RememberLeftPaneWidth(width.IsAbsolute ? width.Value : listColumn.ActualWidth);
            }));
    }
}
