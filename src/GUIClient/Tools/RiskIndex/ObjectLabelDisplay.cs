using System.Globalization;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>An object's name as shown: the label, or "‹Tipo› #id" with the "Requer a permissão ‹módulo›" hint.</summary>
public sealed record ObjectLabelText(string Label, string? Hint)
{
    public bool IsRedacted => Hint is not null;
}

/// <summary>
/// How every panel of the module names an object (S40 §5.0, §11.3). The server already redacts a label the caller may not
/// read (<see cref="RiskIndexObjectLabelDto.RequiresPermission"/>); the client shows it as "‹Tipo› #id" in the interface's
/// language with the permission it requires, never the server's English placeholder.
/// </summary>
public static class ObjectLabelDisplay
{
    public const string RequiresPermissionKey = "RiskCategoryRequiresPermissionFMT";

    public static ObjectLabelText Of(RiskIndexObjectLabelDto label, TextLookup text, CultureInfo culture)
    {
        if (label.RequiresPermission is not { Length: > 0 } permission) return new ObjectLabelText(label.Label, null);
        return new ObjectLabelText(Redacted(label.Object, text), RiskText.Format(text, RequiresPermissionKey, culture, permission));
    }

    /// <summary>The label alone, redacted as <see cref="Of"/> does; "—" for none.</summary>
    public static string Name(RiskIndexObjectLabelDto? label, TextLookup text, CultureInfo culture) =>
        label is null ? "—" : Of(label, text, culture).Label;

    /// <summary>"Cenário #12": the kind's name and the reference.</summary>
    public static string Redacted(RiskIndexObjectRef obj, TextLookup text) =>
        RiskText.Get(text, "RiskObjectKind_" + obj.Kind) + " #" + obj.Ref;
}
