using System.Text;
using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SyncContracts;
using WebSite.Controllers;
using WebSite.Tests.Sync;
using WebSiteData.Sync;
using Xunit;

namespace WebSite.Tests.Controllers;

/// <summary>
/// Exercises the five sync endpoints end-to-end through the real
/// <see cref="SyncSignatureVerifier"/> (a concrete, non-virtual class) over an in-memory SQLite
/// store, with only <see cref="ISyncApplyService"/> substituted.
/// </summary>
[TestSubject(typeof(SyncController))]
public class SyncControllerTest : IDisposable
{
    private readonly SqliteDbContextFactory _factory = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly SyncSignatureVerifier _verifier;
    private readonly ISyncApplyService _apply = Substitute.For<ISyncApplyService>();
    private readonly SyncKeyPair _key = SyncKeyPair.Create("website-key-01");
    private int _nonceCounter;

    public SyncControllerTest()
    {
        _verifier = new SyncSignatureVerifier(_factory, _cache);
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
    }

    /// <summary>Builds a controller whose request carries <paramref name="body"/> plus a valid
    /// signature over <paramref name="path"/>, unless <paramref name="signWith"/> is a foreign
    /// key or <paramref name="unsigned"/> is set.</summary>
    private SyncController BuildController(string path, byte[] body, SyncKeyPair? signWith = null,
        bool unsigned = false)
    {
        var controller = new SyncController(NullLogger<SyncController>.Instance, _apply, _verifier);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = path;
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(body, writable: false);

        if (!unsigned)
        {
            var signer = signWith ?? _key;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = $"nonce-{Interlocked.Increment(ref _nonceCounter)}-{Guid.NewGuid():N}";
            httpContext.Request.Headers[SyncHeaders.KeyId] = _key.KeyId;
            httpContext.Request.Headers[SyncHeaders.Timestamp] = timestamp.ToString();
            httpContext.Request.Headers[SyncHeaders.Nonce] = nonce;
            httpContext.Request.Headers[SyncHeaders.Signature] =
                SyncSignature.Sign(signer.PrivateKeyPem, "POST", path, timestamp, nonce, body);
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static byte[] Json<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
    private static byte[] Garbage() => Encoding.UTF8.GetBytes("this is not json at all {{{");

    // ---------------- Enroll (unsigned, TOFU) ----------------

    [Fact]
    public async Task TestEnrollAcceptsFirstRequest()
    {
        var request = new EnrollRequest { KeyId = "k1", PublicKeyPem = _key.PublicKeyPem };
        _apply.TryEnrollAsync(Arg.Any<EnrollRequest>()).Returns(true);
        var controller = BuildController(SyncRoutes.Enroll, Json(request), unsigned: true);

        var result = await controller.Enroll();

        Assert.IsType<OkResult>(result);
        await _apply.Received(1).TryEnrollAsync(Arg.Is<EnrollRequest>(r => r.KeyId == "k1"));
    }

    [Fact]
    public async Task TestEnrollReturnsConflictWhenAlreadyEnrolled()
    {
        var request = new EnrollRequest { KeyId = "k1", PublicKeyPem = _key.PublicKeyPem };
        _apply.TryEnrollAsync(Arg.Any<EnrollRequest>()).Returns(false);
        var controller = BuildController(SyncRoutes.Enroll, Json(request), unsigned: true);

        var result = await controller.Enroll();

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("Already enrolled", conflict.Value);
    }

    [Fact]
    public async Task TestEnrollRejectsUndeserializableBody()
    {
        var controller = BuildController(SyncRoutes.Enroll, Garbage(), unsigned: true);

        var result = await controller.Enroll();

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid enroll request", bad.Value);
        await _apply.DidNotReceive().TryEnrollAsync(Arg.Any<EnrollRequest>());
    }

    [Theory]
    [InlineData("", "pem")]
    [InlineData("k1", "")]
    [InlineData("", "")]
    public async Task TestEnrollRejectsIncompletePayload(string keyId, string pem)
    {
        var body = Json(new EnrollRequest { KeyId = keyId, PublicKeyPem = pem });
        var controller = BuildController(SyncRoutes.Enroll, body, unsigned: true);

        var result = await controller.Enroll();

        Assert.IsType<BadRequestObjectResult>(result);
        await _apply.DidNotReceive().TryEnrollAsync(Arg.Any<EnrollRequest>());
    }

    // ---------------- RotateKey ----------------

    [Fact]
    public async Task TestRotateKeyAcceptsRequestSignedWithCurrentKey()
    {
        var rotated = SyncKeyPair.Create("website-key-02");
        var body = Json(new RotateKeyRequest
        {
            NewKeyId = rotated.KeyId, NewPublicKeyPem = rotated.PublicKeyPem
        });
        var controller = BuildController(SyncRoutes.RotateKey, body);

        var result = await controller.RotateKey();

        Assert.IsType<OkResult>(result);
        await _apply.Received(1)
            .RotateKeyAsync(Arg.Is<RotateKeyRequest>(r => r.NewKeyId == rotated.KeyId));
    }

    [Fact]
    public async Task TestRotateKeyRejectsUnsignedRequest()
    {
        var body = Json(new RotateKeyRequest { NewKeyId = "x", NewPublicKeyPem = "pem" });
        var controller = BuildController(SyncRoutes.RotateKey, body, unsigned: true);

        var result = await controller.RotateKey();

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Missing signature headers", unauthorized.Value);
        await _apply.DidNotReceive().RotateKeyAsync(Arg.Any<RotateKeyRequest>());
    }

    [Fact]
    public async Task TestRotateKeyRejectsSignatureFromForeignKey()
    {
        var body = Json(new RotateKeyRequest { NewKeyId = "x", NewPublicKeyPem = "pem" });
        var controller = BuildController(SyncRoutes.RotateKey, body,
            signWith: SyncKeyPair.Create());

        var result = await controller.RotateKey();

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Invalid signature", unauthorized.Value);
        await _apply.DidNotReceive().RotateKeyAsync(Arg.Any<RotateKeyRequest>());
    }

    [Fact]
    public async Task TestRotateKeyRejectsUndeserializableBody()
    {
        var controller = BuildController(SyncRoutes.RotateKey, Garbage());

        var result = await controller.RotateKey();

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid rotate request", bad.Value);
        await _apply.DidNotReceive().RotateKeyAsync(Arg.Any<RotateKeyRequest>());
    }

    [Theory]
    [InlineData("", "pem")]
    [InlineData("k2", "")]
    public async Task TestRotateKeyRejectsIncompletePayload(string keyId, string pem)
    {
        var body = Json(new RotateKeyRequest { NewKeyId = keyId, NewPublicKeyPem = pem });
        var controller = BuildController(SyncRoutes.RotateKey, body);

        var result = await controller.RotateKey();

        Assert.IsType<BadRequestObjectResult>(result);
        await _apply.DidNotReceive().RotateKeyAsync(Arg.Any<RotateKeyRequest>());
    }

    // ---------------- Push ----------------

    [Fact]
    public async Task TestPushAppliesSignedPayloadAndReturnsOutbox()
    {
        var actionId = Guid.NewGuid();
        var response = new SyncResponse
        {
            AppliedCursor = 77,
            Actions =
            [
                new OutboxActionDto
                {
                    ClientActionId = actionId,
                    ActionType = SyncActionTypes.CommentCreate,
                    PayloadJson = "{}"
                }
            ]
        };
        _apply.ApplyPushAsync(Arg.Any<PushPayload>()).Returns(response);
        var body = Json(new PushPayload { Cursor = 77 });
        var controller = BuildController(SyncRoutes.Push, body);

        var result = await controller.Push();

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = Assert.IsType<SyncResponse>(ok.Value);
        Assert.Equal(77, value.AppliedCursor);
        Assert.Equal(actionId, Assert.Single(value.Actions).ClientActionId);
        await _apply.Received(1).ApplyPushAsync(Arg.Is<PushPayload>(p => p.Cursor == 77));
    }

    [Fact]
    public async Task TestPushRejectsSignatureFromForeignKey()
    {
        var body = Json(new PushPayload { Cursor = 1 });
        var controller = BuildController(SyncRoutes.Push, body, signWith: SyncKeyPair.Create());

        var result = await controller.Push();

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Invalid signature", unauthorized.Value);
        await _apply.DidNotReceive().ApplyPushAsync(Arg.Any<PushPayload>());
    }

    [Fact]
    public async Task TestPushRejectsUnsignedRequest()
    {
        var body = Json(new PushPayload { Cursor = 1 });
        var controller = BuildController(SyncRoutes.Push, body, unsigned: true);

        var result = await controller.Push();

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _apply.DidNotReceive().ApplyPushAsync(Arg.Any<PushPayload>());
    }

    [Fact]
    public async Task TestPushRejectsUndeserializableBody()
    {
        var controller = BuildController(SyncRoutes.Push, Garbage());

        var result = await controller.Push();

        Assert.IsType<BadRequestResult>(result);
        await _apply.DidNotReceive().ApplyPushAsync(Arg.Any<PushPayload>());
    }

    [Fact]
    public async Task TestPushRejectsJsonNullBody()
    {
        var controller = BuildController(SyncRoutes.Push, Encoding.UTF8.GetBytes("null"));

        var result = await controller.Push();

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task TestPushSignedForAnotherRouteIsRejected()
    {
        // The path is part of the signed canonical string, so a signature minted for /sync/fast
        // must not be accepted on /sync/push.
        var body = Json(new PushPayload { Cursor = 1 });
        var controller = BuildController(SyncRoutes.Fast, body);

        var result = await controller.Push();

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Invalid signature", unauthorized.Value);
    }

    // ---------------- Fast ----------------

    [Fact]
    public async Task TestFastAppliesSignedPayload()
    {
        _apply.ApplyFastAsync(Arg.Any<FastPushPayload>())
            .Returns(new SyncResponse { AppliedCursor = 5 });
        var body = Json(new FastPushPayload { DeletedLinkIds = [1, 2, 3] });
        var controller = BuildController(SyncRoutes.Fast, body);

        var result = await controller.Fast();

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = Assert.IsType<SyncResponse>(ok.Value);
        Assert.Equal(5, value.AppliedCursor);
        await _apply.Received(1)
            .ApplyFastAsync(Arg.Is<FastPushPayload>(p => p.DeletedLinkIds.Count == 3));
    }

    [Fact]
    public async Task TestFastRejectsSignatureFromForeignKey()
    {
        var body = Json(new FastPushPayload());
        var controller = BuildController(SyncRoutes.Fast, body, signWith: SyncKeyPair.Create());

        var result = await controller.Fast();

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _apply.DidNotReceive().ApplyFastAsync(Arg.Any<FastPushPayload>());
    }

    [Fact]
    public async Task TestFastRejectsUndeserializableBody()
    {
        var controller = BuildController(SyncRoutes.Fast, Garbage());

        var result = await controller.Fast();

        Assert.IsType<BadRequestResult>(result);
        await _apply.DidNotReceive().ApplyFastAsync(Arg.Any<FastPushPayload>());
    }

    // ---------------- Ack ----------------

    [Fact]
    public async Task TestAckAppliesSignedRequest()
    {
        var id = Guid.NewGuid();
        var body = Json(new AckRequest { AckedActionIds = [id] });
        var controller = BuildController(SyncRoutes.Ack, body);

        var result = await controller.Ack();

        Assert.IsType<OkResult>(result);
        await _apply.Received(1)
            .ApplyAckAsync(Arg.Is<AckRequest>(a => a.AckedActionIds.Contains(id)));
    }

    [Fact]
    public async Task TestAckRejectsUnsignedRequest()
    {
        var body = Json(new AckRequest());
        var controller = BuildController(SyncRoutes.Ack, body, unsigned: true);

        var result = await controller.Ack();

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _apply.DidNotReceive().ApplyAckAsync(Arg.Any<AckRequest>());
    }

    [Fact]
    public async Task TestAckRejectsUndeserializableBody()
    {
        var controller = BuildController(SyncRoutes.Ack, Garbage());

        var result = await controller.Ack();

        Assert.IsType<BadRequestResult>(result);
        await _apply.DidNotReceive().ApplyAckAsync(Arg.Any<AckRequest>());
    }

    // ---------------- replay across endpoints ----------------

    [Fact]
    public async Task TestReplayingTheSameSignedPushIsRejected()
    {
        _apply.ApplyPushAsync(Arg.Any<PushPayload>()).Returns(new SyncResponse());
        var body = Json(new PushPayload { Cursor = 9 });

        var first = BuildController(SyncRoutes.Push, body);
        var firstResult = await first.Push();

        // Replay the exact headers of the first request with the same body.
        var replay = new SyncController(NullLogger<SyncController>.Instance, _apply, _verifier);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = SyncRoutes.Push;
        httpContext.Request.Body = new MemoryStream(body, writable: false);
        foreach (var header in first.ControllerContext.HttpContext.Request.Headers)
            httpContext.Request.Headers[header.Key] = header.Value;
        replay.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var replayResult = await replay.Push();

        Assert.IsType<OkObjectResult>(firstResult);
        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(replayResult);
        Assert.Equal("Replayed nonce", unauthorized.Value);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _factory.Dispose();
    }
}
