namespace GUIClient.Tools.Hosts;

/// <summary>
/// One choice in a facet ComboBox. "All" is the option with neither <see cref="Number"/> nor
/// <see cref="Text"/>; it is a real item rather than a null selection, because a ComboBox writes
/// null into its binding whenever its items are refilled and that write has to be ignorable.
/// </summary>
/// <param name="Label">What the user reads.</param>
/// <param name="Number">The numeric value the facet filters on (status, team id, criticality).</param>
/// <param name="Text">The text value the facet filters on (environment, history field/actor).</param>
public sealed record FilterOption(string Label, int? Number = null, string? Text = null)
{
    public bool IsAll => Number is null && string.IsNullOrEmpty(Text);
}

/// <summary>One criticality choice in the host edit dialog; <see cref="Level"/> null is "Not set".</summary>
public sealed record CriticalityOption(int? Level, string Label);
