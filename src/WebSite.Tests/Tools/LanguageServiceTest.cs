using JetBrains.Annotations;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebSite.Tools;
using Xunit;

namespace WebSite.Tests.Tools;

/// <summary>
/// Uses a real <see cref="ResourceManagerStringLocalizerFactory"/> configured the same way
/// <c>ServicesBootstrapper</c> configures it (<c>ResourcesPath = "Resources"</c>), so the
/// embedded <c>WebSite.Resources.Localization</c> resource is actually resolved.
/// </summary>
[TestSubject(typeof(LanguageService))]
public class LanguageServiceTest
{
    private static LanguageService Build()
    {
        var options = Options.Create(new LocalizationOptions { ResourcesPath = "Resources" });
        var factory = new ResourceManagerStringLocalizerFactory(options, NullLoggerFactory.Instance);
        return new LanguageService(factory);
    }

    [Fact]
    public void TestGetLocalizedStringResolvesAnExistingResourceKey()
    {
        var service = Build();

        var localized = service.GetLocalizedString("Welcome");

        Assert.Equal("Welcome", localized.Name);
        Assert.False(localized.ResourceNotFound);
        Assert.False(string.IsNullOrEmpty(localized.Value));
    }

    [Fact]
    public void TestIndexerMatchesGetLocalizedString()
    {
        var service = Build();

        Assert.Equal(service.GetLocalizedString("Welcome").Value, service["Welcome"].Value);
    }

    [Fact]
    public void TestUnknownKeyFallsBackToTheKeyItself()
    {
        var service = Build();

        var localized = service.GetLocalizedString("__no_such_resource_key__");

        Assert.True(localized.ResourceNotFound);
        Assert.Equal("__no_such_resource_key__", localized.Value);
    }
}
