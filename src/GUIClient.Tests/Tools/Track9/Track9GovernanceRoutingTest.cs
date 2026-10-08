using GUIClient.Tools.Track9;
using Xunit;

namespace GUIClient.Tests.Tools.Track9;

public class Track9GovernanceRoutingTest
{
    [Theory]
    [InlineData(41, 7, 7, 41)]
    [InlineData(42, null, null, 42)]
    [InlineData(43, 7, 8, null)]
    [InlineData(null, 7, 7, null)]
    public void AppetiteTailEditorOnlyReceivesTheMatchingSavedAppetite(
        int? appetiteId, int? appetiteEntityId, int? editorEntityId, int? expected)
    {
        Assert.Equal(expected,
            Track9GovernanceRouting.AppetiteIdForEditor(appetiteId, appetiteEntityId, editorEntityId));
    }

    [Theory]
    [InlineData(31, 17, 31, 17)]
    [InlineData(32, null, 32, null)]
    public void SelectedTaskLookupsRouteTheChosenMitigationAndOptionalOwner(
        int mitigationId, int? ownerId, int expectedMitigationId, int? expectedOwnerId)
    {
        var accepted = Track9GovernanceRouting.TryCreateTaskRoute(mitigationId, ownerId, out var route);

        Assert.True(accepted);
        Assert.Equal(expectedMitigationId, route.MitigationId);
        Assert.Equal(expectedOwnerId, route.OwnerId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void MissingOrInvalidMitigationCannotCreateATaskRoute(int? mitigationId)
    {
        Assert.False(Track9GovernanceRouting.TryCreateTaskRoute(mitigationId, 17, out _));
    }
}
