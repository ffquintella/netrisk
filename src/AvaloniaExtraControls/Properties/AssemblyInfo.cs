using Avalonia.Metadata;

// The controls below replace the ones NetRisk used to take from the Aura.UI submodule. Aura.UI
// mapped its control namespace onto the default Avalonia xmlns, so views wrote <GroupBox> and
// <Badge> with no prefix. Keeping that mapping here is what lets the submodule go without touching
// a single view.
[assembly: XmlnsDefinition("https://github.com/avaloniaui", "AvaloniaExtraControls.Controls")]
