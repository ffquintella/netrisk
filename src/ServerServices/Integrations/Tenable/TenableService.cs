using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Integrations;
using Model.Secrets;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace ServerServices.Integrations.Tenable;

/// <summary>The Tenable Vulnerability Management integration (M56, T265–T268; S40 §6.8, §8.7, §9.4).</summary>
public interface ITenableService
{
    IReadOnlyList<TenableProductDto> GetProducts();

    Task<List<TenableConnectionView>> GetConnectionsAsync(CancellationToken ct = default);

    Task<TenableConnectionView> GetConnectionAsync(int id, CancellationToken ct = default);

    Task<TenableConnectionView> CreateConnectionAsync(TenableConnectionUpsert request, CancellationToken ct = default);

    Task<TenableConnectionView> UpdateConnectionAsync(int id, TenableConnectionUpsert request, CancellationToken ct = default);

    /// <summary>Deletes the connection and its tag rules; the hosts and findings it imported stay.</summary>
    Task DeleteConnectionAsync(int id, CancellationToken ct = default);

    Task<TenableTestResult> TestConnectionAsync(int id, CancellationToken ct = default);

    /// <summary>Writes a Queued <c>integration_sync_logs</c> row and returns; the job host runs it (S40 §8.7).</summary>
    Task<TenableSyncQueuedDto> QueueSyncAsync(int id, CancellationToken ct = default);

    Task<List<IntegrationSyncLog>> GetSyncLogAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>The job host's tick: claims the queued manual requests, then runs the connections that are due.</summary>
    Task<TenableSyncResult> RunPendingAsync(DateTime nowUtc, CancellationToken ct = default);
}

/// <summary>A refusal of /Tenable with its stable code (S40 §8.7), mapped by the controller to <see cref="Status"/>.</summary>
public sealed class TenableException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;

    public static TenableException BadRequest(string code, string message) => new(400, code, message);
    public static TenableException NotFound(string message) => new(404, TenableErrors.NotFound, message);
    public static TenableException Conflict(string code, string message) => new(409, code, message);
}

/// <summary>The stable error codes of /Tenable (S40 §8.7).</summary>
public static class TenableErrors
{
    public const string BaseUrlInvalid = "base_url_invalid";
    public const string CredentialsRequired = "credentials_required";
    public const string ProductNotSupported = "product_not_supported";
    public const string VaultReferenceInvalid = "vault_reference_invalid";
    public const string InvalidParameter = "invalid_parameter";
    public const string NameTaken = "connection_name_taken";
    public const string EntityNotFound = "connection_entity_not_found";
    public const string SyncRunning = "sync_running";
    public const string ConnectionDisabled = "connection_disabled";
    public const string NotFound = "connection_not_found";
}

/// <summary>
/// T265: the connection, its probe and its queued manual sync. Keys are written by <see cref="ISecretProtector.Protect"/>
/// (a vault reference is stored as given) and read only by <see cref="ISecretResolver.ResolveAsync"/> at each run; no view,
/// log line, sync-log row or connection column ever carries one (<see cref="Redact"/> scrubs every message built here).
/// </summary>
public partial class TenableService(
    ILogger logger,
    IDalService dalService,
    ISecretProtector protector,
    ISecretResolver resolver,
    ITenableClient client,
    IFindingIngestionService ingestion,
    IFindingLifecycleService lifecycle,
    IIntegrationSyncTracker tracker,
    IExploitationSignalsSyncService exploitationSignals)
    : ServiceBase(logger, dalService), ITenableService
{
    /// <summary><c>hosts.external_provider</c> and <c>host_external_ids.provider</c> of a Tenable VM asset.</summary>
    public const string ProviderName = "TenableVulnerabilityManagement";

    /// <summary>The <c>audit_logs.actor</c> of every host the asset sync writes (S38 §5.4).</summary>
    public const string ImportAuditActor = "Tenable import";

    public const int MinIntervalHours = 1, MaxIntervalHours = 168;
    public const int MinWindowDays = 30, MaxWindowDays = 365;

    public IReadOnlyList<TenableProductDto> GetProducts() =>
    [
        new(TenableProduct.VulnerabilityManagement, "TenableProduct_VulnerabilityManagement",
            TenableClient.VulnerabilityManagementUrl, false),
        new(TenableProduct.SecurityCenter, "TenableProduct_SecurityCenter", null, true)
    ];

    public async Task<List<TenableConnectionView>> GetConnectionsAsync(CancellationToken ct = default)
    {
        await using var db = DalService.GetContext();
        var rows = await db.TenableConnections.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }

    public async Task<TenableConnectionView> GetConnectionAsync(int id, CancellationToken ct = default)
    {
        await using var db = DalService.GetContext();
        return ToView(await LoadAsync(db, id, ct));
    }

    public async Task<TenableConnectionView> CreateConnectionAsync(TenableConnectionUpsert request,
        CancellationToken ct = default)
    {
        await using var db = DalService.GetContext();
        var (name, baseUrl) = await ValidateAsync(db, request, null, ct);

        var row = new TenableConnection { CreatedAt = DateTime.UtcNow };
        Copy(request, row, name, baseUrl);
        ApplyKeys(request, row);

        if (string.IsNullOrEmpty(row.EncryptedAccessKey) || string.IsNullOrEmpty(row.EncryptedSecretKey))
            throw TenableException.BadRequest(TenableErrors.CredentialsRequired,
                "A Tenable connection needs both an access key and a secret key, typed or from a vault.");

        db.TenableConnections.Add(row);
        await db.SaveChangesAsync(ct);

        Logger.Information("Tenable connection {Id} ({Name}) created", row.Id, row.Name);
        return ToView(row);
    }

    public async Task<TenableConnectionView> UpdateConnectionAsync(int id, TenableConnectionUpsert request,
        CancellationToken ct = default)
    {
        await using var db = DalService.GetContext();
        var row = await LoadAsync(db, id, ct);
        var (name, baseUrl) = await ValidateAsync(db, request, id, ct);

        Copy(request, row, name, baseUrl);
        ApplyKeys(request, row);
        row.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        Logger.Information("Tenable connection {Id} ({Name}) updated", row.Id, row.Name);
        return ToView(row);
    }

    public async Task DeleteConnectionAsync(int id, CancellationToken ct = default)
    {
        await using var db = DalService.GetContext();
        var row = await LoadAsync(db, id, ct);

        // The connection's tag rules go with it, as Vision One's do: connection_id is not a foreign key, and a rule left
        // behind would apply to whichever connection reuses the id. Hosts, findings and readings stay (S40 §6.8).
        db.VendorEntityMappings.RemoveRange(db.VendorEntityMappings.Where(m =>
            m.Provider == IntegrationKind.Tenable && m.ConnectionId == id));
        db.TenableConnections.Remove(row);
        await db.SaveChangesAsync(ct);

        Logger.Information("Tenable connection {Id} ({Name}) deleted", id, row.Name);
    }

    public async Task<TenableTestResult> TestConnectionAsync(int id, CancellationToken ct = default)
    {
        TenableConnection connection;
        await using (var db = DalService.GetContext())
        {
            connection = await LoadAsync(db, id, ct);
        }

        TenableCredentials? credentials = null;
        try
        {
            credentials = await ResolveCredentialsAsync(connection, ct);
            return Scrub(await client.TestAsync(connection, credentials, ct), credentials);
        }
        catch (Exception ex) when (ex is SecretProtectionException or SecretVaultResolutionException or TenableException)
        {
            // A reference that will not resolve, or a key that will not decrypt, is a failed first step — the message names
            // the reference or the column, never a value.
            return new TenableTestResult(false,
                [new TenableTestStepDto("credentials", false, "TenableTest_CredentialsUnreadable", Redact(ex.Message, credentials))]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning("Testing Tenable connection {Id} threw: {Message}", id, Redact(ex.Message, credentials));
            return new TenableTestResult(false,
                [new TenableTestStepDto("authentication", false, "TenableTest_Failed", Redact(ex.Message, credentials))]);
        }
    }

    public async Task<TenableSyncQueuedDto> QueueSyncAsync(int id, CancellationToken ct = default)
    {
        await using var db = DalService.GetContext();
        var connection = await LoadAsync(db, id, ct);

        if (!connection.Enabled)
            throw TenableException.Conflict(TenableErrors.ConnectionDisabled, "The Tenable connection is disabled.");

        // A Running row the sweep would settle does not count, so a dead run never refuses a manual retry for long.
        await IntegrationSyncLedger.ReapAbandonedAsync(db, IntegrationKind.Tenable, DateTime.UtcNow, id, ct);

        if (await db.IntegrationSyncLogs.AnyAsync(l => l.Integration == IntegrationKind.Tenable && l.ConnectionId == id &&
                                                      (l.Status == IntegrationSyncStatus.Queued ||
                                                       l.Status == IntegrationSyncStatus.Running), ct))
            throw TenableException.Conflict(TenableErrors.SyncRunning,
                "A synchronization of this connection is already queued or running.");

        var row = new IntegrationSyncLog
        {
            Integration = IntegrationKind.Tenable, ConnectionId = id, ConnectionName = connection.Name,
            StartedAt = DateTime.UtcNow, Status = IntegrationSyncStatus.Queued,
            Summary = "Queued: the job host runs it on its next tick."
        };
        db.IntegrationSyncLogs.Add(row);
        await db.SaveChangesAsync(ct);

        Logger.Information("Tenable sync of connection {Id} queued as log {Log}", id, row.Id);
        return new TenableSyncQueuedDto(row.Id);
    }

    public async Task<List<IntegrationSyncLog>> GetSyncLogAsync(int limit = 50, CancellationToken ct = default)
    {
        await using var db = DalService.GetContext();
        return await db.IntegrationSyncLogs.AsNoTracking()
            .Where(l => l.Integration == IntegrationKind.Tenable)
            .OrderByDescending(l => l.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
    }

    // --- helpers -------------------------------------------------------------------------------------------------------

    private async Task<TenableCredentials> ResolveCredentialsAsync(TenableConnection connection, CancellationToken ct)
    {
        var access = await resolver.ResolveAsync(connection.EncryptedAccessKey, ct);
        var secret = await resolver.ResolveAsync(connection.EncryptedSecretKey, ct);

        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(secret))
            throw TenableException.BadRequest(TenableErrors.CredentialsRequired,
                "The connection has no access key or no secret key stored.");

        return new TenableCredentials(access, secret);
    }

    /// <summary>Removes the keys and the header they form from a message (S40 §11.6), whatever path built it.</summary>
    internal static string Redact(string? message, TenableCredentials? credentials) =>
        TenableClient.Redact(message, credentials) ?? string.Empty;

    private static TenableTestResult Scrub(TenableTestResult result, TenableCredentials credentials) =>
        result with
        {
            Steps = result.Steps.Select(s => s with { Detail = s.Detail is null ? null : Redact(s.Detail, credentials) }).ToList()
        };

    private static async Task<(string Name, string BaseUrl)> ValidateAsync(NRDbContext db, TenableConnectionUpsert? request,
        int? self, CancellationToken ct)
    {
        if (request is null) throw TenableException.BadRequest(TenableErrors.InvalidParameter, "A body is required.");

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120)
            throw TenableException.BadRequest(TenableErrors.InvalidParameter, "The name is required, up to 120 characters.");

        if (request.Product != TenableProduct.VulnerabilityManagement)
            throw TenableException.BadRequest(TenableErrors.ProductNotSupported,
                "Only Tenable Vulnerability Management is supported; Security Center is T269 (backlog).");

        // Vulnerability Management has one fixed root (S40 §6.8): any other URL is refused rather than silently replaced.
        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? TenableClient.VulnerabilityManagementUrl : request.BaseUrl.Trim();
        if (!string.Equals(baseUrl.TrimEnd('/'), TenableClient.VulnerabilityManagementUrl, StringComparison.OrdinalIgnoreCase))
            throw TenableException.BadRequest(TenableErrors.BaseUrlInvalid,
                $"Tenable Vulnerability Management is reached only at {TenableClient.VulnerabilityManagementUrl}.");

        if (request.AllowInvalidCertificate)
            throw TenableException.BadRequest(TenableErrors.InvalidParameter,
                "Accepting an invalid certificate is a Security Center option (T269), never Vulnerability Management's.");

        if (request.SyncIntervalHours is < MinIntervalHours or > MaxIntervalHours)
            throw TenableException.BadRequest(TenableErrors.InvalidParameter,
                $"The interval is between {MinIntervalHours} and {MaxIntervalHours} hours.");
        if (request.InitialWindowDays is < MinWindowDays or > MaxWindowDays)
            throw TenableException.BadRequest(TenableErrors.InvalidParameter,
                $"The initial window is between {MinWindowDays} and {MaxWindowDays} days.");
        if (request.MinSeverity is < 0 or > 4)
            throw TenableException.BadRequest(TenableErrors.InvalidParameter,
                "The minimum severity is 0 (info) to 4 (critical).");

        foreach (var reference in new[] { request.AccessKeyVaultReference, request.SecretKeyVaultReference })
            if (!string.IsNullOrWhiteSpace(reference) && !SecretReference.TryParse(reference.Trim(), out _))
                throw TenableException.BadRequest(TenableErrors.VaultReferenceInvalid,
                    "A vault reference must be a vault:v1 reference to a configured vault connection.");

        if (await db.TenableConnections.AsNoTracking().AnyAsync(c => c.Name == name && c.Id != (self ?? 0), ct))
            throw TenableException.Conflict(TenableErrors.NameTaken, "Another Tenable connection has this name.");

        if (request.EntityId is { } entity && !await db.Entities.AsNoTracking().AnyAsync(e => e.Id == entity, ct))
            throw TenableException.BadRequest(TenableErrors.EntityNotFound, "The default entity does not exist.");

        return (name, TenableClient.VulnerabilityManagementUrl);
    }

    private static void Copy(TenableConnectionUpsert request, TenableConnection row, string name, string baseUrl)
    {
        row.Name = name;
        row.Product = request.Product;
        row.BaseUrl = baseUrl;
        row.EntityId = request.EntityId;
        row.Enabled = request.Enabled;
        row.SyncIntervalHours = request.SyncIntervalHours;
        row.SyncFindings = request.SyncFindings;
        row.SyncAssets = request.SyncAssets;
        row.CollectAcr = request.CollectAcr;
        row.CollectAes = request.CollectAes;
        row.MinSeverity = request.MinSeverity;
        row.InitialWindowDays = request.InitialWindowDays;
        row.IncludeUnlicensed = request.IncludeUnlicensed;
        row.AllowInvalidCertificate = false;
    }

    /// <summary>A typed key or a vault reference replaces the stored one; <c>Clear…</c> erases it; neither keeps it.</summary>
    private void ApplyKeys(TenableConnectionUpsert request, TenableConnection row)
    {
        row.EncryptedAccessKey = NextKey(row.EncryptedAccessKey, request.AccessKey, request.AccessKeyVaultReference,
            request.ClearAccessKey);
        row.EncryptedSecretKey = NextKey(row.EncryptedSecretKey, request.SecretKey, request.SecretKeyVaultReference,
            request.ClearSecretKey);
    }

    private string? NextKey(string? stored, string? typed, string? reference, bool clear)
    {
        if (!string.IsNullOrWhiteSpace(reference)) return protector.Protect(reference.Trim());
        if (!string.IsNullOrEmpty(typed)) return protector.Protect(typed);
        return clear ? null : stored;
    }

    private static async Task<TenableConnection> LoadAsync(NRDbContext db, int id, CancellationToken ct) =>
        await db.TenableConnections.FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw TenableException.NotFound($"Tenable connection {id} was not found.");

    private static TenableConnectionView ToView(TenableConnection c) =>
        new(c.Id, c.Name, c.Product, c.BaseUrl, c.EntityId, c.Enabled, c.SyncIntervalHours, c.SyncFindings, c.SyncAssets,
            c.CollectAcr, c.CollectAes, c.MinSeverity, c.InitialWindowDays, c.IncludeUnlicensed, c.AllowInvalidCertificate,
            !string.IsNullOrEmpty(c.EncryptedAccessKey), !string.IsNullOrEmpty(c.EncryptedSecretKey),
            SecretReference.StoredReferenceOrNull(c.EncryptedAccessKey), SecretReference.StoredReferenceOrNull(c.EncryptedSecretKey),
            c.LastSyncAt, (int?)c.LastSyncStatus, c.LastSyncError);

    private static string? Truncate(string? text, int max) =>
        text == null || text.Length <= max ? text : text[..(max - 1)] + "…";
}
