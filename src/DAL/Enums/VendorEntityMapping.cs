namespace DAL.Enums;

// T263, T268 (S40 §6.6, §7.2, §8.3): the stored vocabularies of vendor_entity_mappings. S40 §8.3 places them under
// Model.Integrations; they live here because the DAL entity stores them and Model references DAL, not the reverse (the
// convention of every other stored enum). Every value is explicit and only ever appended.

/// <summary>What a vendor → entity rule matches on (<c>vendor_entity_mappings.match_kind</c>).</summary>
public enum VendorMatchKind
{
    /// <summary>A Vision One custom asset tag (<c>assetCustomTags</c> of a device): attributes the host to the entity.</summary>
    V1CustomTag = 1,

    /// <summary>
    /// A Vision One asset group (<c>assetGroups</c>). Never attributes a device — the API does not return group membership
    /// per device — it ties the group's sub-index to the node as a reference indicator (S40 §5.2.4), which T271 collects.
    /// </summary>
    V1AssetGroup = 2,

    /// <summary>A Tenable asset tag (<c>category:value</c>), T268; not accepted until the Tenable connection exists.</summary>
    TenableTag = 3
}

/// <summary>Whether a matching rule may move a host that already has an entity (<c>restamp_policy</c>, S39 D-20).</summary>
public enum RestampPolicy
{
    /// <summary>Writes the entity only on a host with none (the default).</summary>
    OnlyUnassigned = 1,

    /// <summary>Rewrites the entity whenever the winning rule points elsewhere; every rewrite goes through the Host audit.</summary>
    FollowMapping = 2
}
