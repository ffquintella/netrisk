using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using JetBrains.Annotations;
using NSubstitute;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// With the FaceID plugin off, selecting a user on the Users screen called <c>GET /FaceID/faceSet/{id}</c> anyway, and
/// the endpoint answered 401 for a caller that is not the user (or an administrator), logged as an error each time.
/// </summary>
[TestSubject(typeof(FaceIdEnrollmentLoader))]
public class FaceIdEnrollmentLoaderTest
{
    [Fact]
    public async Task PluginInactive_CallsNothingAndReportsNotEnrolled()
    {
        var service = Substitute.For<IFaceIDService>();

        var result = await FaceIdEnrollmentLoader.LoadAsync(service, pluginActive: false, userId: 7);

        Assert.Equal(new FaceIdEnrollment(false, false), result);
        await service.DidNotReceiveWithAnyArgs().IsUserEnabledAsync(default);
        await service.DidNotReceiveWithAnyArgs().UserHasFaceSetAsync(default);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task PluginActive_ReadsBothFlagsForTheUser(bool enabled, bool faceSet)
    {
        var service = Substitute.For<IFaceIDService>();
        service.IsUserEnabledAsync(7).Returns(enabled);
        service.UserHasFaceSetAsync(7).Returns(faceSet);

        var result = await FaceIdEnrollmentLoader.LoadAsync(service, pluginActive: true, userId: 7);

        Assert.Equal(new FaceIdEnrollment(enabled, faceSet), result);
    }

    /// <summary>A save with the plugin off sent <c>GET /FaceID/enable|disable/{id}</c> anyway: one round trip for nothing.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveWithPluginInactive_WritesNothing(bool enabled)
    {
        var service = Substitute.For<IFaceIDService>();

        await FaceIdEnrollmentLoader.SaveEnabledAsync(service, pluginActive: false, userId: 7, enabled);

        await service.DidNotReceiveWithAnyArgs().SetUserEnabledStatusAsync(default, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveWithPluginActive_WritesTheFlagForTheUser(bool enabled)
    {
        var service = Substitute.For<IFaceIDService>();

        await FaceIdEnrollmentLoader.SaveEnabledAsync(service, pluginActive: true, userId: 7, enabled);

        await service.Received(1).SetUserEnabledStatusAsync(7, enabled);
    }
}
