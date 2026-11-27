using System.Collections.Generic;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.DTO;
using NSubstitute;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// A save on the Users screen used to be three requests — the user, the FaceID flag, the permissions — each paying the
/// server's full authentication cost. An existing user and their permissions now go in one request; the FaceID flag only
/// while the plugin is active; a new user is still created first, since its permissions need its id.
/// </summary>
[TestSubject(typeof(UserSaveSequence))]
public class UserSaveSequenceTest
{
    private readonly IUsersService _users = Substitute.For<IUsersService>();
    private readonly IFaceIDService _faceId = Substitute.For<IFaceIDService>();

    private static List<Permission?> Grants() =>
        [new Permission { Id = 1, Key = "riskmanagement", Name = "r", Description = "" }];

    [Fact]
    public async Task ExistingUser_IsSavedWithItsPermissionsInOneRequest()
    {
        var user = new UserDto { Id = 7, Name = "Ana" };
        var grants = Grants();

        var saved = await UserSaveSequence.RunAsync(_users, _faceId, faceIdPluginActive: false, user, grants, false);

        Assert.Same(user, saved);
        await _users.Received(1).SaveUserWithPermissionsAsync(user, grants);
        _users.DidNotReceiveWithAnyArgs().SaveUser(default!);
        _users.DidNotReceiveWithAnyArgs().SaveUserPermissions(default, default!);
        _users.DidNotReceiveWithAnyArgs().CreateUser(default!);
    }

    [Fact]
    public async Task ExistingUser_WithThePluginOff_WritesNoFaceIdFlag()
    {
        await UserSaveSequence.RunAsync(_users, _faceId, faceIdPluginActive: false, new UserDto { Id = 7 }, Grants(), true);

        await _faceId.DidNotReceiveWithAnyArgs().SetUserEnabledStatusAsync(default, default);
    }

    [Fact]
    public async Task ExistingUser_WithThePluginOn_WritesTheFaceIdFlag()
    {
        await UserSaveSequence.RunAsync(_users, _faceId, faceIdPluginActive: true, new UserDto { Id = 7 }, Grants(), true);

        await _faceId.Received(1).SetUserEnabledStatusAsync(7, true);
    }

    [Fact]
    public async Task NewUser_IsCreatedThenGivenItsPermissionsUnderTheNewId()
    {
        var draft = new UserDto { Id = 0, Name = "New" };
        var created = new UserDto { Id = 42, Name = "New" };
        _users.CreateUser(draft).Returns(created);
        var grants = Grants();

        var saved = await UserSaveSequence.RunAsync(_users, _faceId, faceIdPluginActive: true, draft, grants, true);

        Assert.Same(created, saved);
        Received.InOrder(() =>
        {
            _users.CreateUser(draft);
            _faceId.SetUserEnabledStatusAsync(42, true);
            _users.SaveUserPermissions(42, grants);
        });
        await _users.DidNotReceiveWithAnyArgs().SaveUserWithPermissionsAsync(default!, default!);
    }

    [Fact]
    public async Task NewUser_WithThePluginOff_WritesNoFaceIdFlag()
    {
        _users.CreateUser(Arg.Any<UserDto>()).Returns(new UserDto { Id = 42 });

        await UserSaveSequence.RunAsync(_users, _faceId, faceIdPluginActive: false, new UserDto { Id = 0 }, Grants(), true);

        await _faceId.DidNotReceiveWithAnyArgs().SetUserEnabledStatusAsync(default, default);
        _users.Received(1).SaveUserPermissions(42, Arg.Any<List<Permission?>>());
    }

    [Fact]
    public async Task ARefusalOfTheSaveSurfacesAndWritesNoFaceIdFlag()
    {
        _users.SaveUserWithPermissionsAsync(Arg.Any<UserDto>(), Arg.Any<List<Permission?>>())
            .Returns(Task.FromException(new Model.Exceptions.RuleBrokenException("refused", "rule")));

        await Assert.ThrowsAsync<Model.Exceptions.RuleBrokenException>(() =>
            UserSaveSequence.RunAsync(_users, _faceId, faceIdPluginActive: true, new UserDto { Id = 7 }, Grants(), true));

        await _faceId.DidNotReceiveWithAnyArgs().SetUserEnabledStatusAsync(default, default);
    }
}
