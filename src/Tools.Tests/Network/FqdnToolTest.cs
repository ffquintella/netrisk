using JetBrains.Annotations;
using Tools.Network;
using Xunit;

namespace Tools.Tests.Network;

[TestSubject(typeof(FqdnTool))]
public class FqdnToolTest
{

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("subdomain.example.com", true)]
    [InlineData("invalid_domain", false)]
    [InlineData("", false)]
    public void IsValidTest(string fqdn, bool expected)
    {
        var result = FqdnTool.IsValid(fqdn);
        Assert.Equal(expected, result);
    }
}