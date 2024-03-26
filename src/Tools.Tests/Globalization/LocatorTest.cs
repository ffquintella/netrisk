using System.Globalization;
using System.Reflection;
using System.Resources;
using JetBrains.Annotations;
using Tools.Globalization;
using Xunit;

namespace Tools.Tests.Globalization;

[TestSubject(typeof(Locator))]
public class LocatorTest
{
    private readonly Locator _locator;

    public LocatorTest()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceManager = new ResourceManager("Tools.Tests.Resources.Localization", assembly);
        _locator = new Locator(assembly, resourceManager, new CultureInfo("en-US"));
    }

    [Theory]
    [InlineData("Hello", "Hello")]
    [InlineData("Goodbye", "Goodbye")]
    public void GetStringTest(string name, string expected)
    {
        var result = _locator[name];
        Assert.Equal(expected, result.Value);
    }

}