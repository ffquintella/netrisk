using System;
using System.IO;
using GUIClient;
using Xunit;

namespace GUIClient.Tests;

/// <summary>
/// The client's configuration files live next to its executable, but nothing guarantees the
/// process starts there: Finder launches a macOS app bundle with the current directory set to
/// <c>/</c>. Resolving appsettings.json against the current directory therefore aborted the
/// installed client at start-up with "'/appsettings.json' was not found" — and silently ignored a
/// deployed netrisk.ini. These tests pin the lookup to the install directory.
/// </summary>
public class ClientConfigurationBaseDirectoryTest
{
    [Fact]
    public void TheBaseDirectoryIsTheOneHoldingTheClientAssemblies()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ClientConfigurationSources).Assembly.Location)!;

        Assert.Equal(
            Path.GetFullPath(assemblyDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(ClientConfigurationSources.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("ConfigurationBootstrapper.cs")]
    [InlineData("Program.cs")]
    public void ConfigurationIsNeverResolvedAgainstTheCurrentDirectory(string file)
    {
        var source = File.ReadAllText(Path.Combine(GuiClientSourceRoot(), file));

        Assert.DoesNotContain("GetCurrentDirectory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.CurrentDirectory", source, StringComparison.Ordinal);
        Assert.Contains("ClientConfigurationSources.BaseDirectory", source, StringComparison.Ordinal);
    }

    private static string GuiClientSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");
            if (File.Exists(project))
                return Path.GetDirectoryName(project)!;
        }

        throw new InvalidOperationException(
            $"Could not find GUIClient/GUIClient.csproj walking up from {AppContext.BaseDirectory}.");
    }
}
