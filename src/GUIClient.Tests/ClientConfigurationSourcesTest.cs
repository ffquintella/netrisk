using System;
using System.IO;
using System.Linq;
using GUIClient;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GUIClient.Tests;

/// <summary>
/// The installers pre-seed settings — above all the server URL — by writing the optional
/// netrisk.ini overlay next to the executable (the Windows MSI does it from its SERVERURL
/// property). That only works if the client layers the file last and treats it as optional, so
/// both properties are pinned here.
/// </summary>
public class ClientConfigurationSourcesTest
{
    [Fact]
    public void TheOverlayIsTheLastLayerInReleaseSoADeployedSettingWins()
    {
        var sources = ClientConfigurationSources.Release;

        Assert.Equal("appsettings.json", sources[0].FileName);
        Assert.Equal(ClientConfigurationSources.OverlayIniFileName, sources[^1].FileName);
        Assert.Equal(ClientConfigurationFormat.Ini, sources[^1].Format);
    }

    [Fact]
    public void TheOverlayIsTheLastLayerInDevelopmentToo()
    {
        var sources = ClientConfigurationSources.Development;

        Assert.Equal("appsettings.development.json", sources[0].FileName);
        Assert.Equal(ClientConfigurationSources.OverlayIniFileName, sources[^1].FileName);
    }

    [Fact]
    public void TheOverlayIsOptionalBecauseMostInstallsNeverGetOne()
    {
        // An installer only writes it when the administrator passed a server URL; a missing
        // file must not stop the client from starting.
        foreach (var sources in new[] { ClientConfigurationSources.Release, ClientConfigurationSources.Development })
            Assert.True(sources.Single(s => s.Format == ClientConfigurationFormat.Ini).Optional);
    }

    [Fact]
    public void TheShippedAppSettingsIsRequiredSoAMissingOneFailsLoudly()
    {
        foreach (var sources in new[] { ClientConfigurationSources.Release, ClientConfigurationSources.Development })
            Assert.False(sources.First().Optional);
    }

    [Fact]
    public void TheOverlayFileNameIsTheOneTheInstallersWrite() =>
        // The MSI IniFile row, the Flatpak/Snap docs and this constant have to agree.
        Assert.Equal("netrisk.ini", ClientConfigurationSources.OverlayIniFileName);

    [Fact]
    public void EachSetDeclaresExactlyOneJsonFileAndOneIniOverlay()
    {
        foreach (var sources in new[] { ClientConfigurationSources.Release, ClientConfigurationSources.Development })
        {
            Assert.Equal(1, sources.Count(s => s.Format == ClientConfigurationFormat.Json));
            Assert.Equal(1, sources.Count(s => s.Format == ClientConfigurationFormat.Ini));
        }
    }
}

/// <summary>
/// Proves the mechanism the Windows MSI's <c>SERVERURL</c> property relies on: an
/// administrator-written netrisk.ini next to the executable really does override the shipped
/// appsettings.json. Builds the same provider stack <c>ConfigurationBootstrapper</c> builds,
/// over a temporary directory, so no installer or GUI is involved.
/// </summary>
public class ClientConfigurationLayeringTest : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "netrisk-cfg-" + Guid.NewGuid().ToString("N"))).FullName;

    private const string ShippedAppSettings =
        """
        {
          "Server": { "Url": "https://127.0.0.1:5443/", "Description": "Server 1", "Timeout": "00:00:10" }
        }
        """;

    private IConfigurationRoot Build()
    {
        var builder = new ConfigurationBuilder().SetBasePath(_directory);

        foreach (var source in ClientConfigurationSources.Release)
        {
            builder = source.Format switch
            {
                ClientConfigurationFormat.Ini => builder.AddIniFile(source.FileName, source.Optional),
                _ => builder.AddJsonFile(source.FileName, source.Optional)
            };
        }

        return builder.Build();
    }

    [Fact]
    public void WithoutAnOverlayTheShippedDefaultIsUsed()
    {
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"), ShippedAppSettings);

        Assert.Equal("https://127.0.0.1:5443/", Build()["Server:Url"]);
    }

    [Fact]
    public void TheOverlayOverridesTheShippedServerUrl()
    {
        // This is exactly what `msiexec /i … SERVERURL="https://netrisk.example.com:5443/"`
        // writes through the MSI IniFile table.
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"), ShippedAppSettings);
        File.WriteAllText(Path.Combine(_directory, ClientConfigurationSources.OverlayIniFileName),
            "[Server]\nUrl=https://netrisk.example.com:5443/\n");

        var configuration = Build();

        Assert.Equal("https://netrisk.example.com:5443/", configuration["Server:Url"]);
        // Settings the overlay does not mention keep their shipped values.
        Assert.Equal("00:00:10", configuration["Server:Timeout"]);
    }

    [Fact]
    public void TheIniSectionMapsToTheServerConfigurationPath()
    {
        // The INI provider turns "[Server] Url" into "Server:Url", which is why the MSI needs
        // no NetRisk-specific parsing. If that ever stopped holding, the property would write a
        // file the client silently ignores.
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"), ShippedAppSettings);
        File.WriteAllText(Path.Combine(_directory, ClientConfigurationSources.OverlayIniFileName),
            "[Server]\nDescription=Homolog\n");

        Assert.Equal("Homolog", Build().GetSection("Server")["Description"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
