using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// A seal or chip of the card with the object-list filter its "›" applies (§5.2.2, §5.9.1). It opens only a filter the list
/// accepts as a preset (<see cref="ObjectListQuery.IsValidPreset"/>), so a server key the client does not know stays text.
/// </summary>
public sealed record HeadlineLink(string Key, string Text, string? ObjectFilter)
{
    public bool CanOpen => ObjectListQuery.IsValidPreset(ObjectFilter);
}

/// <summary>The card's seals and chips with their filters (T255: the "›" of §5.2.2 → the object list's preset).</summary>
public static class HeadlineLinks
{
    public static IReadOnlyList<HeadlineLink> Seals(IReadOnlyList<RiskIndexSealDto> seals, TextLookup text, CultureInfo culture) =>
        seals.Zip(HeadlineFormatter.Seals(seals, text, culture), (s, t) => new HeadlineLink(s.Key, t, s.ObjectFilter)).ToList();

    public static IReadOnlyList<HeadlineLink> Chips(IReadOnlyList<RiskIndexChipDto> chips,
        IReadOnlyList<RiskIndexCategoryDto> categories, TextLookup text, CultureInfo culture) =>
        HeadlineFormatter.Chips(chips, categories, text, culture)
            .Select(c => new HeadlineLink(c.Key, c.Text, chips.FirstOrDefault(d => d.Key == c.Key)?.ObjectFilter)).ToList();
}
