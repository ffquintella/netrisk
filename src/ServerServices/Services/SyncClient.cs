using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Serilog;
using ServerServices.Interfaces;
using SyncContracts;

namespace ServerServices.Services;

public class SyncClient : ISyncClient
{
    private readonly ILogger _logger;
    private readonly ISyncKeyService _keyService;

    public SyncClient(ILogger logger, ISyncKeyService keyService)
    {
        _logger = logger;
        _keyService = keyService;
    }

    private static HttpClient CreateClient(string websiteUrl, bool insecure)
    {
        var handler = new HttpClientHandler();
        if (insecure)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        return new HttpClient(handler) { BaseAddress = new Uri(websiteUrl), Timeout = TimeSpan.FromMinutes(2) };
    }

    public async Task<bool> EnrollAsync(string websiteUrl, bool insecure = false, CancellationToken ct = default)
    {
        using var client = CreateClient(websiteUrl, insecure);
        var body = new EnrollRequest { KeyId = _keyService.GetKeyId(), PublicKeyPem = _keyService.GetPublicKeyPem() };
        // Enrollment is unsigned by design (TOFU): the website has no key yet.
        var response = await client.PostAsJsonAsync(SyncRoutes.Enroll, body, ct);
        if (response.IsSuccessStatusCode) return true;
        _logger.Error("Enrollment failed: {Status} {Reason}", (int)response.StatusCode, response.ReasonPhrase);
        return false;
    }

    public async Task<bool> RotateAsync(string websiteUrl, bool insecure = false, CancellationToken ct = default)
    {
        var next = _keyService.PrepareRotation();
        var body = new RotateKeyRequest { NewKeyId = next.KeyId, NewPublicKeyPem = next.PublicKeyPem };
        using var client = CreateClient(websiteUrl, insecure);
        // Signed with the CURRENT key, proving control of the trusted private key.
        var response = await PostSignedAsync(client, SyncRoutes.RotateKey, body, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.Error("Key rotation rejected by website: {Status}", (int)response.StatusCode);
            return false;
        }
        _keyService.CommitRotation(next.KeyId, next.PublicKeyPem, next.PrivateKeyPem);
        return true;
    }

    public Task<SyncResponse?> PushAsync(string websiteUrl, PushPayload payload, bool insecure = false, CancellationToken ct = default)
        => SendForResponseAsync(websiteUrl, SyncRoutes.Push, payload, insecure, ct);

    public Task<SyncResponse?> FastPushAsync(string websiteUrl, FastPushPayload payload, bool insecure = false, CancellationToken ct = default)
        => SendForResponseAsync(websiteUrl, SyncRoutes.Fast, payload, insecure, ct);

    public async Task<bool> AckAsync(string websiteUrl, AckRequest ack, bool insecure = false, CancellationToken ct = default)
    {
        using var client = CreateClient(websiteUrl, insecure);
        var response = await PostSignedAsync(client, SyncRoutes.Ack, ack, ct);
        return response.IsSuccessStatusCode;
    }

    private async Task<SyncResponse?> SendForResponseAsync(string websiteUrl, string path, object payload, bool insecure, CancellationToken ct)
    {
        using var client = CreateClient(websiteUrl, insecure);
        var response = await PostSignedAsync(client, path, payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.Error("Sync push to {Path} failed: {Status}", path, (int)response.StatusCode);
            return null;
        }
        var json = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(json)
            ? new SyncResponse()
            : JsonSerializer.Deserialize<SyncResponse>(json);
    }

    private async Task<HttpResponseMessage> PostSignedAsync(HttpClient client, string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        var bodyBytes = Encoding.UTF8.GetBytes(json);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Guid.NewGuid().ToString("N");
        var signature = _keyService.Sign("POST", path, timestamp, nonce, bodyBytes);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add(SyncHeaders.KeyId, _keyService.GetKeyId());
        request.Headers.Add(SyncHeaders.Timestamp, timestamp.ToString());
        request.Headers.Add(SyncHeaders.Nonce, nonce);
        request.Headers.Add(SyncHeaders.Signature, signature);

        return await client.SendAsync(request, ct);
    }
}
