using System.Collections.Generic;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace GUIClient.Models.Reports;

/// <summary>
/// Editable representation of a single report section used by the template designer.
/// Serializes to/from the backend's <c>ReportSection</c> JSON contract
/// (<c>{ Type, Content, TableColumns }</c>).
/// </summary>
public class ReportSectionEditModel : ReactiveObject
{
    /// <summary>Section types the QuestPDF renderer understands.</summary>
    public static readonly ObservableCollection<string> AvailableTypes = new() { "Title", "Text", "Table" };

    private string _type = "Text";
    public string Type
    {
        get => _type;
        set => this.RaiseAndSetIfChanged(ref _type, value);
    }

    private string? _content;
    public string? Content
    {
        get => _content;
        set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    // Comma-separated column names for Table sections; empty means "auto-discover all columns".
    private string? _tableColumnsText;
    public string? TableColumnsText
    {
        get => _tableColumnsText;
        set => this.RaiseAndSetIfChanged(ref _tableColumnsText, value);
    }

    public ObservableCollection<string> AvailableTypesList => AvailableTypes;

    public List<string>? ToTableColumns()
    {
        if (string.IsNullOrWhiteSpace(TableColumnsText)) return null;
        var cols = new List<string>();
        foreach (var part in TableColumnsText.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0) cols.Add(trimmed);
        }
        return cols.Count == 0 ? null : cols;
    }
}
