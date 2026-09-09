namespace GUIClient.Tools;

/// <summary>
/// How the scope of an entity-scoped governance row is written in a grid cell (Track 8 milestone
/// 8.3.3).
///
/// Three cases, and each of the other two is a bug this exists to stop coming back. A row whose
/// entity id is null is not scoped to an entity at all — it is the organization-wide row — and a
/// blank cell there reads as missing data rather than as "the whole organization"; the appetite grid
/// showed exactly that blank. A row whose entity has no <c>name</c> property, or whose property bag
/// was not loaded, has no name to show, and a blank cell there is worse still: it hides which scope
/// the row governs. So the fallback is the id, marked as one.
///
/// A free function with no Avalonia types, so <c>GUIClient.Tests</c> can compile it directly — that
/// project deliberately does not reference <c>GUIClient</c>. The localized "Global" string is passed
/// in because a value converter or a compiled binding on the row cannot reach the view model that
/// holds it.
/// </summary>
public static class EntityScopeLabel
{
    /// <summary>
    /// Renders the scope of one row.
    /// </summary>
    /// <param name="entityId">The row's entity id, or null for the organization-wide row.</param>
    /// <param name="entityName">The entity's human name, when it is known.</param>
    /// <param name="globalLabel">The localized word for the organization-wide scope.</param>
    public static string Describe(int? entityId, string? entityName, string globalLabel)
    {
        if (entityId is null) return globalLabel;

        return string.IsNullOrWhiteSpace(entityName) ? $"#{entityId.Value}" : entityName;
    }
}
