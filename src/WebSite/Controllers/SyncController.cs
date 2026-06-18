using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SyncContracts;
using WebSiteData.Sync;

namespace WebSite.Controllers;

/// <summary>
/// The website's passive sync surface. The server initiates every exchange and authenticates
/// with an ECDSA signature verified against the enrolled public key. Enrollment is TOFU.
/// </summary>
[ApiController]
[Route("sync")]
public class SyncController : ControllerBase
{
    private readonly ILogger<SyncController> _logger;
    private readonly ISyncApplyService _apply;
    private readonly SyncSignatureVerifier _verifier;

    public SyncController(ILogger<SyncController> logger, ISyncApplyService apply, SyncSignatureVerifier verifier)
    {
        _logger = logger;
        _apply = apply;
        _verifier = verifier;
    }

    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll()
    {
        var body = await ReadBodyAsync();
        var request = Deserialize<EnrollRequest>(body);
        if (request == null || string.IsNullOrEmpty(request.PublicKeyPem) || string.IsNullOrEmpty(request.KeyId))
            return BadRequest("Invalid enroll request");

        var enrolled = await _apply.TryEnrollAsync(request);
        if (!enrolled)
        {
            _logger.LogWarning("Enroll rejected: website already enrolled (TOFU)");
            return Conflict("Already enrolled");
        }
        _logger.LogInformation("Website enrolled with key {KeyId}", request.KeyId);
        return Ok();
    }

    [HttpPost("rotate-key")]
    public async Task<IActionResult> RotateKey()
    {
        var body = await ReadBodyAsync();
        // The rotate request must be signed with the CURRENT enrolled key.
        var check = await VerifyAsync(SyncRoutes.RotateKey, body);
        if (!check.Ok) return Unauthorized(check.Error);

        var request = Deserialize<RotateKeyRequest>(body);
        if (request == null || string.IsNullOrEmpty(request.NewPublicKeyPem) || string.IsNullOrEmpty(request.NewKeyId))
            return BadRequest("Invalid rotate request");

        await _apply.RotateKeyAsync(request);
        _logger.LogInformation("Sync key rotated to {KeyId}", request.NewKeyId);
        return Ok();
    }

    [HttpPost("push")]
    public async Task<IActionResult> Push()
    {
        var body = await ReadBodyAsync();
        var check = await VerifyAsync(SyncRoutes.Push, body);
        if (!check.Ok) return Unauthorized(check.Error);

        var payload = Deserialize<PushPayload>(body);
        if (payload == null) return BadRequest();

        var response = await _apply.ApplyPushAsync(payload);
        return Ok(response);
    }

    [HttpPost("fast")]
    public async Task<IActionResult> Fast()
    {
        var body = await ReadBodyAsync();
        var check = await VerifyAsync(SyncRoutes.Fast, body);
        if (!check.Ok) return Unauthorized(check.Error);

        var payload = Deserialize<FastPushPayload>(body);
        if (payload == null) return BadRequest();

        var response = await _apply.ApplyFastAsync(payload);
        return Ok(response);
    }

    [HttpPost("ack")]
    public async Task<IActionResult> Ack()
    {
        var body = await ReadBodyAsync();
        var check = await VerifyAsync(SyncRoutes.Ack, body);
        if (!check.Ok) return Unauthorized(check.Error);

        var ack = Deserialize<AckRequest>(body);
        if (ack == null) return BadRequest();

        await _apply.ApplyAckAsync(ack);
        return Ok();
    }

    private Task<SignatureCheck> VerifyAsync(string path, byte[] body) => _verifier.VerifyAsync(
        "POST", path,
        Request.Headers[SyncHeaders.KeyId],
        Request.Headers[SyncHeaders.Timestamp],
        Request.Headers[SyncHeaders.Nonce],
        Request.Headers[SyncHeaders.Signature],
        body);

    private async Task<byte[]> ReadBodyAsync()
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static T? Deserialize<T>(byte[] body) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body);
        }
        catch
        {
            return null;
        }
    }
}
