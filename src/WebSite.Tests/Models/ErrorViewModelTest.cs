using JetBrains.Annotations;
using WebSite.Models;
using Xunit;

namespace WebSite.Tests.Models;

[TestSubject(typeof(ErrorViewModel))]
public class ErrorViewModelTest
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", true)]
    [InlineData("0HN7A", true)]
    public void TestShowRequestId(string? requestId, bool expected)
    {
        var model = new ErrorViewModel { RequestId = requestId };

        Assert.Equal(expected, model.ShowRequestId);
    }

    [Fact]
    public void TestRequestIdDefaultsToNull()
    {
        var model = new ErrorViewModel();

        Assert.Null(model.RequestId);
        Assert.False(model.ShowRequestId);
    }
}
