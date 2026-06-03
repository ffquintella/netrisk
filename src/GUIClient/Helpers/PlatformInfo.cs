using System.Runtime.InteropServices;

namespace GUIClient.Helpers;

/// <summary>
/// Centralised, cheap platform probes used to drive platform-native ergonomics
/// (global menu redirection, window-control alignment, tray behaviour).
/// Mirrors the inline <see cref="RuntimeInformation"/> checks that were scattered
/// across the code-base so view-models can bind to the booleans directly.
/// </summary>
public static class PlatformInfo
{
    public static bool IsMacOS { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static bool IsLinux { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>True on every platform except macOS – handy for XAML IsVisible bindings.</summary>
    public static bool IsNotMacOS => !IsMacOS;
}
