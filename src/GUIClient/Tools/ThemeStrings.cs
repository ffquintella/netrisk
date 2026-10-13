using System;
using System.Collections.Generic;

namespace GUIClient.Tools;

/// <summary>
/// The user-facing strings baked into the Semi.Avalonia control templates, re-sourced from the
/// client's own <c>Localization*.resx</c>.
///
/// Semi localizes its templates through <c>{DynamicResource STRING_*}</c> keys — the DatePicker's
/// day/month/year placeholders, the TimePicker's hour/minute/second, the TextBox context menu, the
/// ScrollBar menu, the managed file chooser. A <c>SemiTheme</c> with no <c>Locale</c> loads its
/// <c>zh-CN</c> dictionary, so every date field in the client rendered 月 日 年. Setting
/// <c>Locale</c> alone is not the fix: Semi ships no pt-BR dictionary, so a Portuguese session
/// would fall back to Chinese (or, at best, English) placeholders under Portuguese labels.
///
/// So App.axaml pins <c>Locale="en-US"</c> as the floor for any key a future Semi adds, and
/// App.Initialize writes every key below into <c>Application.Resources</c>, which a resource lookup
/// consults before the theme's own dictionaries. Avalonia-free so GUIClient.Tests can compile it.
/// </summary>
public static class ThemeStrings
{
    /// <summary>Semi's resource key → the client's resx key that supplies its text.</summary>
    public static readonly IReadOnlyDictionary<string, string> SemiKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["STRING_DATEPICKER_DAY_TEXT"] = "ThemeDatePickerDay",
        ["STRING_DATEPICKER_MONTH_TEXT"] = "ThemeDatePickerMonth",
        ["STRING_DATEPICKER_YEAR_TEXT"] = "ThemeDatePickerYear",
        ["STRING_TIMEPICKER_HOUR_TEXT"] = "ThemeTimePickerHour",
        ["STRING_TIMEPICKER_MINUTE_TEXT"] = "ThemeTimePickerMinute",
        ["STRING_TIMEPICKER_SECOND_TEXT"] = "ThemeTimePickerSecond",
        ["STRING_MENU_CUT"] = "ThemeMenuCut",
        ["STRING_MENU_COPY"] = "ThemeMenuCopy",
        ["STRING_MENU_PASTE"] = "ThemeMenuPaste",
        ["STRING_DRAWERPAGE_TOGGLE_NAVIGATION_DRAWER"] = "ThemeDrawerToggleNavigation",
        ["STRING_CHOOSER_FILE_NAME"] = "ThemeChooserFileName",
        ["STRING_CHOOSER_SHOW_HIDDEN_FILES"] = "ThemeChooserShowHiddenFiles",
        ["STRING_CHOOSER_DIALOG_OK"] = "ThemeChooserOk",
        ["STRING_CHOOSER_DIALOG_CANCEL"] = "ThemeChooserCancel",
        ["STRING_CHOOSER_NAME_COLUMN"] = "ThemeChooserNameColumn",
        ["STRING_CHOOSER_DATEMODIFIED_COLUMN"] = "ThemeChooserDateModifiedColumn",
        ["STRING_CHOOSER_TYPE_COLUMN"] = "ThemeChooserTypeColumn",
        ["STRING_CHOOSER_SIZE_COLUMN"] = "ThemeChooserSizeColumn",
        ["STRING_CHOOSER_PROMPT_FILE_ALREADY_EXISTS"] = "ThemeChooserFileAlreadyExists",
        ["STRING_SCROLLBAR_SCROLL_HERE"] = "ThemeScrollBarScrollHere",
        ["STRING_SCROLLBAR_TOP"] = "ThemeScrollBarTop",
        ["STRING_SCROLLBAR_BOTTOM"] = "ThemeScrollBarBottom",
        ["STRING_SCROLLBAR_PAGE_UP"] = "ThemeScrollBarPageUp",
        ["STRING_SCROLLBAR_PAGE_DOWN"] = "ThemeScrollBarPageDown",
        ["STRING_SCROLLBAR_SCROLL_UP"] = "ThemeScrollBarScrollUp",
        ["STRING_SCROLLBAR_SCROLL_DOWN"] = "ThemeScrollBarScrollDown",
        ["STRING_SCROLLBAR_LEFT_EDGE"] = "ThemeScrollBarLeftEdge",
        ["STRING_SCROLLBAR_RIGHT_EDGE"] = "ThemeScrollBarRightEdge",
        ["STRING_SCROLLBAR_PAGE_LEFT"] = "ThemeScrollBarPageLeft",
        ["STRING_SCROLLBAR_PAGE_RIGHT"] = "ThemeScrollBarPageRight",
        ["STRING_SCROLLBAR_SCROLL_LEFT"] = "ThemeScrollBarScrollLeft",
        ["STRING_SCROLLBAR_SCROLL_RIGHT"] = "ThemeScrollBarScrollRight",
    };

    /// <summary>
    /// Semi key → localized text. A key the localizer cannot resolve (null, empty, or echoed back as
    /// its own name) is left out, so the theme's <c>en-US</c> text shows instead of a resx key name.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> Resolve(Func<string, string?> localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        foreach (var (semiKey, resxKey) in SemiKeys)
        {
            var text = localize(resxKey);
            if (string.IsNullOrEmpty(text) || string.Equals(text, resxKey, StringComparison.Ordinal))
                continue;

            yield return new KeyValuePair<string, string>(semiKey, text);
        }
    }
}
