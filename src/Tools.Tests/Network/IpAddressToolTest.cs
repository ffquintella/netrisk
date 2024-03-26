using JetBrains.Annotations;
using Tools.Network;
using Xunit;

namespace Tools.Tests.Network;

[TestSubject(typeof(IpAddressTool))]
public class IpAddressToolTest
{
    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("256.0.0.0", false)]
    [InlineData("", false)]
    public void IsValidTest(string address, bool expected)
    {
        var result = IpAddressTool.IsValid(address);
        Assert.Equal(expected, result);
    }

}