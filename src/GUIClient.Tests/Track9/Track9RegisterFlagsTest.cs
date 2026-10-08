using System.Collections.Generic;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.RiskFlags;
using Xunit;
namespace GUIClient.Tests.Track9;
public class Track9RegisterFlagsTest
{
    [Fact]
    public void ActiveFlagFilterDoesNotTreatUnavailableOrUnflaggedRisksAsMatches()
    {
        var flag = (RiskFlagCode)1;
        var rows = new Dictionary<int, FlaggedRiskDto> { [7] = new() { RiskId = 7, Flags = [flag] } };
        Assert.True(Track9RegisterFlags.Matches(7, flag, rows));
        Assert.False(Track9RegisterFlags.Matches(8, flag, rows));
        Assert.False(Track9RegisterFlags.Matches(7, (RiskFlagCode)2, rows));
        Assert.False(Track9RegisterFlags.Matches(7, flag, null));
        Assert.True(Track9RegisterFlags.Matches(8, null, null));
    }
}
