using GUIClient.Tools.Hosts;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.Hosts;

/// <summary>The host list footer, <c>first–last of total</c>, and the previous/next buttons.</summary>
[TestSubject(typeof(PageRange))]
public class PageRangeTest
{
    [Fact]
    public void TheFirstFullPageHasOnlyANextPage()
    {
        Assert.Equal(new PageRange(1, 100, 412, false, true), PageRange.Of(1, 100, 412, 100));
    }

    [Fact]
    public void AMiddlePageHasBoth()
    {
        Assert.Equal(new PageRange(101, 200, 412, true, true), PageRange.Of(2, 100, 412, 100));
    }

    [Fact]
    public void TheLastPartialPageHasOnlyAPreviousPage()
    {
        Assert.Equal(new PageRange(401, 412, 412, true, false), PageRange.Of(5, 100, 412, 12));
    }

    [Fact]
    public void AnExactlyFullLastPageHasNoNextPage()
    {
        Assert.Equal(new PageRange(101, 200, 200, true, false), PageRange.Of(2, 100, 200, 100));
    }

    [Fact]
    public void NoResultsIsZeroToZeroWithNeitherDirection()
    {
        Assert.Equal(new PageRange(0, 0, 0, false, false), PageRange.Of(1, 100, 0, 0));
    }

    [Fact]
    public void AnEmptyPageBeyondTheEndStillOffersTheWayBack()
    {
        var range = PageRange.Of(3, 100, 150, 0);

        Assert.True(range.HasPrevious);
        Assert.False(range.HasNext);
    }

    [Fact]
    public void AMissingTotalIsReplacedByWhatIsOnScreen()
    {
        // No X-Total-Count reads as 0; the 37 rows shown are better evidence than that.
        Assert.Equal(new PageRange(1, 37, 37, false, false), PageRange.Of(1, 100, 0, 37));
    }

    [Fact]
    public void APageBelowOneIsPageOne()
    {
        Assert.Equal(PageRange.Of(1, 100, 412, 100), PageRange.Of(0, 100, 412, 100));
    }
}
