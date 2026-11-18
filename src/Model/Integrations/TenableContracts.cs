using System;
using System.Collections.Generic;
using DAL.Enums;

namespace Model.Integrations;

// M56, T265 (S40 §8.3, §8.7): the /Tenable contract. A view never carries a key — only whether one is stored and, for a
// vault-backed key, its reference.

public record TenableProductDto(TenableProduct Product, string NameKey, string? DefaultBaseUrl, bool BaseUrlEditable);

public record TenableConnectionView(int Id, string Name, TenableProduct Product, string BaseUrl, int? EntityId, bool Enabled,
    int SyncIntervalHours, bool SyncFindings, bool SyncAssets, bool CollectAcr, bool CollectAes, int MinSeverity,
    int InitialWindowDays, bool IncludeUnlicensed, bool AllowInvalidCertificate, bool HasAccessKey, bool HasSecretKey,
    string? AccessKeyVaultReference, string? SecretKeyVaultReference, DateTime? LastSyncAtUtc, int? LastSyncStatus,
    string? LastSyncError);

/// <summary>
/// A create or update. A key left null without its <c>Clear…</c> flag keeps the stored one; a <c>…VaultReference</c>
/// stores the reference instead of a typed key; <c>Clear…</c> erases the stored key.
/// </summary>
public record TenableConnectionUpsert(string Name, TenableProduct Product, string? BaseUrl, int? EntityId, bool Enabled,
    int SyncIntervalHours, bool SyncFindings, bool SyncAssets, bool CollectAcr, bool CollectAes, int MinSeverity,
    int InitialWindowDays, bool IncludeUnlicensed, bool AllowInvalidCertificate,
    string? AccessKey, string? SecretKey, string? AccessKeyVaultReference, string? SecretKeyVaultReference,
    bool ClearAccessKey, bool ClearSecretKey);

public record TenableTestResult(bool Success, IReadOnlyList<TenableTestStepDto> Steps);

/// <summary>One probe step; <c>Detail</c> is built from Tenable's answer and never contains a credential.</summary>
public record TenableTestStepDto(string StepKey, bool Passed, string? MessageKey, string? Detail);

public record TenableSyncQueuedDto(int LogId);

/// <summary>The counts of one Tenable run (T265–T268), summed across connections by the scheduled pass.</summary>
public class TenableSyncResult
{
    public int Runs { get; set; }
    public int AssetsReceived { get; set; }
    public int HostsCreated { get; set; }
    public int HostsUpdated { get; set; }
    public int HostsAssignedByRule { get; set; }
    public int HostsReassignedByRule { get; set; }
    public int ScoresWritten { get; set; }
    public int FindingsReceived { get; set; }
    public int FindingsCreated { get; set; }
    public int FindingsUpdated { get; set; }
    public int FindingsFixed { get; set; }
    public int Errors { get; set; }
    public List<string> Messages { get; set; } = new();
}
