using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.FaceID;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// <c>POST /FaceID/transactions/{userId}/commit</c> completes a biometric ceremony and mints a
/// <see cref="FaceToken"/>, so the route user must be the caller — the same rule
/// <c>StartTransaction</c> applies to the other half of the ceremony. Before the fix the action
/// passed the route id straight to the service, so any signed-in user could commit a transaction
/// for anybody else's account.
///
/// The shared caller (<c>testUser</c>, id 1, see <c>MockedUsersService</c>) is an administrator on
/// purpose: neither half of the ceremony has an administrator override, and the refusal below proves
/// none was added.
/// </summary>
[TestSubject(typeof(FaceIDController))]
public class FaceIDControllerTest : BaseControllerTest
{
    private const int CallerId = 1;
    private const int OtherUserId = 2;

    private static FaceTransactionData Transaction(int userId) => new()
    {
        UserId = userId,
        TransactionId = Guid.NewGuid(),
        StartTime = DateTime.UtcNow
    };

    private static (FaceIDController Controller, IFaceIDService FaceId) Build(
        Action<IServiceCollection>? configure = null)
    {
        var faceId = Substitute.For<IFaceIDService>();
        faceId.UserHasFaceSetAsync(Arg.Any<int>()).Returns(true);
        faceId.CommitTransactionAsync(Arg.Any<int>(), Arg.Any<FaceTransactionData>(), Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(new FaceToken { Token = "minted-token" });

        var controller = ResolveController<FaceIDController>(services =>
        {
            services.AddSingleton(faceId);
            configure?.Invoke(services);
        });

        return (controller, faceId);
    }

    [Fact]
    public async Task TestFaceEnrollmentForAnotherUserIsRefusedWithoutQueryingIt()
    {
        var (controller, faceId) = Build();

        var result = await controller.CheckUserHasFaceSet(OtherUserId);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        await faceId.DidNotReceiveWithAnyArgs().UserHasFaceSetAsync(default);
    }

    [Fact]
    public async Task TestCallerCanReadTheirOwnFaceEnrollmentState()
    {
        var (controller, faceId) = Build();

        var result = await controller.CheckUserHasFaceSet(CallerId);

        Assert.True(result.Value);
        await faceId.Received(1).UserHasFaceSetAsync(CallerId);
    }

    [Fact]
    public async Task TestCommitTransactionForAnotherUserIsRefusedAndNothingIsCommitted()
    {
        var (controller, faceId) = Build();

        var result = await controller.CommitTransaction(OtherUserId, Transaction(OtherUserId));

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Null(result.Value);
        await faceId.DidNotReceiveWithAnyArgs().CommitTransactionAsync(default, default!, default, default);
    }

    [Fact]
    public async Task TestCommitTransactionForTheCallersOwnAccountReturnsTheToken()
    {
        var (controller, faceId) = Build();

        var result = await controller.CommitTransaction(CallerId, Transaction(CallerId), "incident");

        Assert.Null(result.Result);
        Assert.NotNull(result.Value);
        Assert.Equal("minted-token", result.Value.Token);
        await faceId.Received(1).CommitTransactionAsync(CallerId, Arg.Any<FaceTransactionData>(), "incident",
            Arg.Any<string?>());
    }

    /// <summary>
    /// A principal with no matching account cannot be compared with the route user, so it is refused
    /// before the service is reached — the same 404 the action already gives an unknown user.
    /// </summary>
    [Fact]
    public async Task TestCommitTransactionByAnUnknownCallerIsNotFoundAndNothingIsCommitted()
    {
        var users = Substitute.For<IUsersService>();
        users.GetUserAsync(Arg.Any<string>()).Returns((User?)null);
        users.GetUser(Arg.Any<string>()).Returns((User?)null);

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Loopback },
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ghost")], "Basic"))
        });

        var (controller, faceId) = Build(services =>
        {
            services.AddSingleton(users);
            services.AddSingleton(accessor);
        });

        var result = await controller.CommitTransaction(CallerId, Transaction(CallerId));

        Assert.IsType<NotFoundObjectResult>(result.Result);
        await faceId.DidNotReceiveWithAnyArgs().CommitTransactionAsync(default, default!, default, default);
    }
}
