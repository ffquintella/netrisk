using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A Tenable connection (M56, T265; S40 §6.8, §7.2 <c>tenable_connections</c>). M56 accepts only Vulnerability Management
/// at the fixed <c>https://cloud.tenable.com</c>; Security Center is T269 (backlog). Both keys are vault-capable (typed
/// ciphertext or a <c>vault:v1:…</c> reference), read only through <c>ISecretResolver</c>, never returned to a client and
/// never recorded by the audit trail.
/// </summary>
public class TenableConnection
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public TenableProduct Product { get; set; } = TenableProduct.VulnerabilityManagement;

    public string BaseUrl { get; set; } = null!;

    /// <summary>The access key, encrypted or a vault reference; with the secret key it forms <c>X-ApiKeys</c>.</summary>
    public string? EncryptedAccessKey { get; set; }

    /// <summary>The secret key, encrypted or a vault reference.</summary>
    public string? EncryptedSecretKey { get; set; }

    /// <summary>The default entity of hosts no tag rule attributes (S40 §6.6); null leaves them unassigned.</summary>
    public int? EntityId { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>1–168 h, default 24; the cadence is anchored on <see cref="NextDueAt"/> (S40 §6.8).</summary>
    public int SyncIntervalHours { get; set; } = 24;

    /// <summary>Runs the vulnerability export (T266).</summary>
    public bool SyncFindings { get; set; } = true;

    /// <summary>Runs the asset export (T267).</summary>
    public bool SyncAssets { get; set; } = true;

    /// <summary>Stores <c>ratings.acr.score</c> in <c>vendor_scores</c>; never writes <c>hosts.criticality</c> (S40 §6.8).</summary>
    public bool CollectAcr { get; set; }

    /// <summary>Stores <c>ratings.aes.score</c> in <c>vendor_scores</c> (S40 §6.8).</summary>
    public bool CollectAes { get; set; }

    /// <summary>The lowest severity exported: 0 info, 1 low (default), 2 medium, 3 high, 4 critical.</summary>
    public int MinSeverity { get; set; } = 1;

    /// <summary>30–365 days, default 90: the <c>since</c> of the first export (without it Tenable returns 30 days).</summary>
    public int InitialWindowDays { get; set; } = 90;

    /// <summary>Also syncs assets without a Tenable licence; off by default.</summary>
    public bool IncludeUnlicensed { get; set; }

    /// <summary>Security Center only (T269); always false for Vulnerability Management.</summary>
    public bool AllowInvalidCertificate { get; set; }

    /// <summary>The <c>since</c> of the next vulnerability export; advances only after the findings step fully succeeds.</summary>
    public DateTime? FindingsCursor { get; set; }

    /// <summary>The <c>updated_at</c> lower bound of the next asset export; advances only after the asset step succeeds.</summary>
    public DateTime? AssetsCursor { get; set; }

    /// <summary>
    /// When the scheduled sync is next due (beyond the S40 §7.2 column list; §6.8's anchored cadence needs it): set on the
    /// first tick after the connection is saved and always advanced from itself by whole intervals. A manual sync does not
    /// move it.
    /// </summary>
    public DateTime? NextDueAt { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public IntegrationSyncStatus? LastSyncStatus { get; set; }

    /// <summary>Built from Tenable's response, never from the request (S40 §11.6).</summary>
    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Entity? Entity { get; set; }
}
