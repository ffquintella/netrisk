namespace DAL.Enums;

// M57, T272 (S40 §7.2 vendor_assets): the stored vocabularies of a vendor's non-host asset. Every value is explicit and only
// ever appended.

/// <summary>What a <c>vendor_assets</c> row is (S40 §7.2 <c>asset_kind</c>) — one per Vision One attack-surface endpoint.</summary>
public enum VendorAssetKind
{
    /// <summary><c>attackSurfaceGlobalFqdns</c> — V1-IFA, EXP.X.</summary>
    Fqdn = 1,

    /// <summary><c>attackSurfacePublicIpAddresses</c> — V1-IFA, EXP.X.</summary>
    PublicIp = 2,

    /// <summary><c>attackSurfaceDomainAccounts</c> — V1-ACC, EXP.I.</summary>
    DomainAccount = 3,

    /// <summary><c>attackSurfaceServiceAccounts</c> — V1-ACC, EXP.I.</summary>
    ServiceAccount = 4,

    /// <summary><c>attackSurfaceCloudAssets</c> — V1-CLD, EXP.N.</summary>
    CloudAsset = 5,

    /// <summary><c>attackSurfaceLocalApps</c> — V1-APP, EXP.N.</summary>
    LocalApp = 6
}

/// <summary>Whether the vendor still reports the asset (S40 §7.2 <c>status</c>).</summary>
public enum VendorAssetStatus
{
    Active = 1,

    /// <summary>Absent from the last complete read of its endpoint; it leaves the index's inventory.</summary>
    Gone = 2
}
