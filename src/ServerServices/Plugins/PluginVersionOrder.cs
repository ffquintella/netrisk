namespace ServerServices.Plugins;

/// <summary>
/// Orders two plugin version strings.
///
/// Needed because a plugin's version is whatever string its <c>PluginVersion</c> property returns,
/// and the administration list has to choose which of two installations of the same plugin to show.
/// Semantic ordering matters for exactly the case this was written for: "1.2.10" is newer than
/// "1.2.9", which an ordinal comparison gets backwards.
/// </summary>
public static class PluginVersionOrder
{
    /// <summary>
    /// Negative when <paramref name="left"/> is the older version, positive when it is the newer,
    /// zero when they order equally.
    /// </summary>
    /// <remarks>
    /// A version that is not dotted numerics ("2026.1-rc1", "") cannot be ordered numerically, so it
    /// falls back to a case-insensitive ordinal comparison rather than being treated as zero -- the
    /// point is a total, stable order, not a correct reading of every versioning scheme anyone might
    /// use.
    /// </remarks>
    public static int Compare(string? left, string? right)
    {
        var l = (left ?? string.Empty).Trim();
        var r = (right ?? string.Empty).Trim();

        if (Version.TryParse(l, out var lv) && Version.TryParse(r, out var rv))
            return lv.CompareTo(rv);

        return string.Compare(l, r, StringComparison.OrdinalIgnoreCase);
    }
}
