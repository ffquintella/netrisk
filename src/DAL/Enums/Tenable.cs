namespace DAL.Enums;

// M56, T265–T267 (S40 §7.2, §8.3): the stored vocabularies of tenable_connections and vendor_scores. S40 §8.3 places
// TenableProduct under Model.Integrations; it lives here because the DAL entity stores it and Model references DAL, not the
// reverse (the convention of VendorMatchKind). Every value is explicit and only ever appended.

/// <summary>Which Tenable product a connection talks to (<c>tenable_connections.product</c>).</summary>
public enum TenableProduct
{
    /// <summary>Tenable Vulnerability Management (cloud, <c>https://cloud.tenable.com</c>): the only product M56 accepts.</summary>
    VulnerabilityManagement = 1,

    /// <summary>Tenable Security Center (on premises): reserved for T269 (backlog); refused until it is built.</summary>
    SecurityCenter = 2
}

/// <summary>Which vendor reading a <c>vendor_scores</c> row holds (S40 §7.2 <c>source</c>).</summary>
public enum VendorScoreSource
{
    V1Device = 1,

    /// <summary>Tenable Asset Exposure Score, 0–1000 (<c>ratings.aes.score</c>); EXP.D when TEN-AES is enabled (T267).</summary>
    TenableAes = 2,

    /// <summary>Tenable Asset Criticality Rating, 1–10 (<c>ratings.acr.score</c>); a criticality candidate when TEN-ACR is enabled (T267).</summary>
    TenableAcr = 3,

    V1Cri = 4,
    V1AssetGroup = 5,
    V1SecurityConfig = 6,
    V1AttackIntensity = 7,
    TenableCes = 8,
    V1VendorAsset = 9
}

/// <summary>What a <c>vendor_scores</c> row is about (S40 §7.2 <c>object_kind</c>).</summary>
public enum VendorScoreObjectKind
{
    Host = 1,
    VendorAsset = 2,
    Tenant = 3,
    AssetGroup = 4
}
