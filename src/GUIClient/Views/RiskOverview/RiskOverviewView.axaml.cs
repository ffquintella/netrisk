using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels.RiskOverview;
using ReactiveUI;

namespace GUIClient.Views.RiskOverview;

/// <summary>
/// The Cyber Risk Overview module (S40 §10.1; T251). The one piece of code-behind: <c>CalendarDatePicker.BlackoutDates</c> is
/// a collection, not a bindable property, so the days without a snapshot (§5.1) are copied onto it here.
/// </summary>
public partial class RiskOverviewView : UserControl
{
    private IDisposable? _subscription;

    public RiskOverviewView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as RiskOverviewViewModel);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Watch(RiskOverviewViewModel? viewModel)
    {
        _subscription?.Dispose();
        _subscription = null;
        if (viewModel is null) return;
        _subscription = viewModel.WhenAnyValue(vm => vm.BlackoutRanges).Subscribe(ApplyBlackout);
    }

    private void ApplyBlackout(IReadOnlyList<(DateOnly From, DateOnly To)> ranges)
    {
        if (this.FindControl<CalendarDatePicker>("SnapshotPicker") is not { } picker) return;

        if (picker.BlackoutDates is not { } blackout) return;

        blackout.Clear();
        foreach (var (from, to) in ranges)
            blackout.Add(new CalendarDateRange(from.ToDateTime(TimeOnly.MinValue), to.ToDateTime(TimeOnly.MinValue)));
    }
}
