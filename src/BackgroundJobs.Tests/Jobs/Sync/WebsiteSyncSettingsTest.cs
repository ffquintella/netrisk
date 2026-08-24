using System;
using System.Threading.Tasks;
using BackgroundJobs.Jobs.Sync;
using JetBrains.Annotations;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.Sync;

[TestSubject(typeof(WebsiteSyncSettings))]
public class WebsiteSyncSettingsTest
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    private void Configure(string key, string value)
    {
        _settings.ConfigurationKeyExistsAsync(key).Returns(Task.FromResult(true));
        _settings.GetConfigurationKeyValueAsync(key).Returns(Task.FromResult(value));
    }

    [Theory]
    [InlineData(0, "*/1 * * * *")]
    [InlineData(-5, "*/1 * * * *")]
    [InlineData(1, "*/1 * * * *")]
    [InlineData(2, "*/2 * * * *")]
    [InlineData(59, "*/59 * * * *")]
    [InlineData(60, "0 */1 * * *")]
    [InlineData(120, "0 */2 * * *")]
    [InlineData(1440, "0 0 * * *")]
    [InlineData(2880, "0 0 * * *")]
    [InlineData(90, "*/90 * * * *")]
    public void TestMinutesToCron(int minutes, string expected)
    {
        Assert.Equal(expected, WebsiteSyncSettings.MinutesToCron(minutes));
    }

    [Fact]
    public async Task TestGetValueAsyncReturnsTheStoredValue()
    {
        Configure(WebsiteSyncSettings.UrlKey, "https://example.invalid");

        var value = await WebsiteSyncSettings.GetValueAsync(_settings, WebsiteSyncSettings.UrlKey);

        Assert.Equal("https://example.invalid", value);
    }

    [Fact]
    public async Task TestGetValueAsyncReturnsNullWhenTheKeyIsAbsent()
    {
        _settings.ConfigurationKeyExistsAsync(WebsiteSyncSettings.UrlKey).Returns(Task.FromResult(false));

        Assert.Null(await WebsiteSyncSettings.GetValueAsync(_settings, WebsiteSyncSettings.UrlKey));
        await _settings.DidNotReceive().GetConfigurationKeyValueAsync(WebsiteSyncSettings.UrlKey);
    }

    [Fact]
    public async Task TestGetValueAsyncSwallowsSettingsFailures()
    {
        _settings.ConfigurationKeyExistsAsync(WebsiteSyncSettings.UrlKey)
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("settings store down"));

        Assert.Null(await WebsiteSyncSettings.GetValueAsync(_settings, WebsiteSyncSettings.UrlKey));
    }

    [Fact]
    public async Task TestGetUrlAsyncReadsTheUrlKey()
    {
        Configure(WebsiteSyncSettings.UrlKey, "https://sync.example.invalid");

        Assert.Equal("https://sync.example.invalid", await WebsiteSyncSettings.GetUrlAsync(_settings));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    public async Task TestGetInsecureAsync(string stored, bool expected)
    {
        Configure(WebsiteSyncSettings.InsecureKey, stored);

        Assert.Equal(expected, await WebsiteSyncSettings.GetInsecureAsync(_settings));
    }

    [Fact]
    public async Task TestGetInsecureAsyncDefaultsToFalseWhenUnset()
    {
        _settings.ConfigurationKeyExistsAsync(WebsiteSyncSettings.InsecureKey).Returns(Task.FromResult(false));

        Assert.False(await WebsiteSyncSettings.GetInsecureAsync(_settings));
    }

    [Fact]
    public void TestDefaultIntervals()
    {
        Assert.Equal(60, WebsiteSyncSettings.DefaultIntervalMinutes);
        Assert.Equal(2, WebsiteSyncSettings.DefaultFastIntervalMinutes);
    }
}
