using System;
using System.IO;
using System.Linq;
using GUIClient;
using Xunit;

namespace GUIClient.Tests;

/// <summary>
/// The per-environment overlay: <c>--environment=homolog</c> layers <c>netrisk.homolog.ini</c>
/// on top of <c>netrisk.ini</c>, so one installation can point each environment at its own server.
/// </summary>
public class EnvironmentOverlayTest
{
    [Fact]
    public void TheEnvironmentOverlayIsLayeredAfterTheSharedOne()
    {
        var sources = ClientConfigurationSources.ForEnvironment(ClientConfigurationSources.Release, "homolog");

        Assert.Equal(ClientConfigurationSources.OverlayIniFileName, sources[^2].FileName);
        Assert.Equal("netrisk.homolog.ini", sources[^1].FileName);
        Assert.Equal(ClientConfigurationFormat.Ini, sources[^1].Format);
        Assert.True(sources[^1].Optional);
    }

    [Fact]
    public void ProductionGetsAnOverlayToo() =>
        Assert.Equal("netrisk.production.ini",
            ClientConfigurationSources.ForEnvironment(ClientConfigurationSources.Release, "production")[^1].FileName);

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData("dev ")]
    public void ANameThatCouldLeaveTheDirectoryAddsNoOverlay(string environment)
    {
        // The name comes straight from the command line and ends up in a file name.
        var sources = ClientConfigurationSources.ForEnvironment(ClientConfigurationSources.Release, environment);

        Assert.Equal(ClientConfigurationSources.Release.Count, sources.Count);
    }

    [Fact]
    public void TheShippedSetsAreLeftAlone()
    {
        ClientConfigurationSources.ForEnvironment(ClientConfigurationSources.Release, "dev");

        Assert.Equal(2, ClientConfigurationSources.Release.Count);
    }
}

/// <summary>
/// A server URL an administrator deploys in the overlays (Puppet, the MSI's SERVERURL) is
/// authoritative: it replaces whatever the user's configuration store holds. Only the overlays
/// count — the shipped appsettings.json carries a 127.0.0.1 placeholder that must never
/// overwrite a real server.
/// </summary>
public class ManagedServerUrlTest : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "netrisk-managed-" + Guid.NewGuid().ToString("N"))).FullName;

    private string Resolve(string environment) =>
        ManagedServerUrl.Resolve(_directory,
            ClientConfigurationSources.ForEnvironment(ClientConfigurationSources.Release, environment))!;

    private void Write(string file, string content) => File.WriteAllText(Path.Combine(_directory, file), content);

    [Fact]
    public void TheShippedAppSettingsIsNeverAManagedUrl()
    {
        Write("appsettings.json", """{ "Server": { "Url": "https://127.0.0.1:5443/" } }""");

        Assert.Null(Resolve("production"));
    }

    [Fact]
    public void TheSharedOverlayProvidesTheUrl()
    {
        Write("netrisk.ini", "[Server]\nUrl=https://netrisk.example.com:5443/\n");

        Assert.Equal("https://netrisk.example.com:5443/", Resolve("production"));
    }

    [Fact]
    public void TheEnvironmentOverlayWinsOverTheSharedOne()
    {
        Write("netrisk.ini", "[Server]\nUrl=https://netrisk.example.com:5443/\n");
        Write("netrisk.homolog.ini", "[Server]\nUrl=https://homolog.example.com:5443/\n");

        Assert.Equal("https://homolog.example.com:5443/", Resolve("homolog"));
        Assert.Equal("https://netrisk.example.com:5443/", Resolve("production"));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://netrisk.example.com/")]
    [InlineData("netrisk.example.com")]
    public void AMalformedUrlIsIgnored(string url)
    {
        Write("netrisk.ini", $"[Server]\nUrl={url}\n");

        Assert.Null(Resolve("production"));
    }

    [Fact]
    public void WithoutOverlaysThereIsNothingToManage() => Assert.Null(Resolve("production"));

    [Theory]
    [InlineData(null, "https://a.example.com/", true)]
    [InlineData("https://old.example.com/", "https://a.example.com/", true)]
    [InlineData("https://a.example.com/", "https://a.example.com/", false)]
    [InlineData("https://user-picked.example.com/", null, false)]
    public void TheStoreIsRewrittenOnlyWhenTheManagedUrlDiffers(string? stored, string? managed, bool expected) =>
        Assert.Equal(expected, ManagedServerUrl.ShouldReplace(stored, managed));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
