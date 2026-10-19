using Avalonia.Controls;
using GUIClient.ViewModels.RiskOverview;

namespace GUIClient.Views.RiskOverview;

/// <summary>
/// The object list (S40 §5.9.1; T254). The sort is the server's: a header click is cancelled here before the grid sorts the
/// page it holds — that would order 50 rows and lie about the set — and handed to the view-model by the column's
/// <c>SortMemberPath</c>, which names the server's sort key. A double click is the row's "›".
/// </summary>
public partial class RiskObjectList : UserControl
{
    public RiskObjectList()
    {
        InitializeComponent();
        ObjectsGrid.Sorting += OnSorting;
        ObjectsGrid.DoubleTapped += (_, _) => (DataContext as RiskObjectListViewModel)?.OpenSelected();
    }

    private void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        (DataContext as RiskObjectListViewModel)?.SortBy(e.Column.SortMemberPath);
    }
}
