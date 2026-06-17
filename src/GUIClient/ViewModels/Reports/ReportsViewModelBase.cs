using System.Collections;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using ReactiveUI;

namespace GUIClient.ViewModels.Reports;

public class ReportsViewModelBase: ViewModelBase
{

    #region LANGUAGE

    public string StrFilters { get; } = Localizer["Filters"];
    public string StrGenerate { get; } = Localizer["Generate"];
    public string StrData { get; } = Localizer["Data"];
    public string StrExport { get; } = Localizer["Export"];

    #endregion

    #region PROPERTIES

    private bool _showFilters = true;
    public bool ShowFilters {
        get => _showFilters;
        set => this.RaiseAndSetIfChanged(ref _showFilters, value);
    }

    #endregion

    #region METHODS

    /// <summary>
    /// Client-side export of a chart's series data ("what you see is what you export")
    /// to CSV or Excel. Used by the dashboard/statistics chart views.
    /// </summary>
    protected async Task ExportSeriesAsync(IEnumerable series)
    {
        if (series == null) return;

        var owner = WindowsManager.AllWindows.Find(w => w is Views.ReportsWindow);

        var format = await ExportFileSaver.PickFormatAsync(
            owner, Localizer["Export"], Localizer["Choose the export format"], includePdf: false);

        if (format is null) return;

        var data = format == ExportFormat.Csv
            ? GridDataExporter.SeriesToCsv(series)
            : GridDataExporter.SeriesToXlsx(series);

        await ExportFileSaver.SaveAsync(owner, format.Value, data);
    }

    #endregion
}
