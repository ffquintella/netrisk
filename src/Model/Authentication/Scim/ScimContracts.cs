using System.Text.Json.Serialization;

namespace Model.Authentication.Scim;

/// <summary>
/// SCIM 2.0 wire types (RFC 7643/7644) for the provisioning endpoints
/// (Track 4 milestone 4.3.2).
///
/// Hand-written rather than generated because the parts NetRisk implements are small and the parts it
/// does not — enterprise extensions, complex multi-valued filters — are better left absent than
/// half-present. The names are the protocol's, so they are camelCase on the wire and the schema URNs
/// are literal strings.
/// </summary>
public static class ScimSchemas
{
    public const string User = "urn:ietf:params:scim:schemas:core:2.0:User";
    public const string Group = "urn:ietf:params:scim:schemas:core:2.0:Group";
    public const string ListResponse = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
    public const string PatchOp = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
    public const string Error = "urn:ietf:params:scim:api:messages:2.0:Error";
    public const string ServiceProviderConfig = "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig";
}

/// <summary>A SCIM user resource.</summary>
public class ScimUser
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = [ScimSchemas.User];

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The IdP's stable identifier for the user. Stored so a rename does not orphan the account.</summary>
    [JsonPropertyName("externalId")]
    public string? ExternalId { get; set; }

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public ScimName? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("emails")]
    public List<ScimEmail> Emails { get; set; } = new();

    /// <summary>
    /// The whole point of SCIM for a security product: <c>false</c> must disable login and revoke
    /// live sessions, not merely record an intent.
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("groups")]
    public List<ScimGroupRef> Groups { get; set; } = new();

    [JsonPropertyName("meta")]
    public ScimMeta? Meta { get; set; }

    /// <summary>The primary email, or the first one, or the userName when it looks like an address.</summary>
    public string? PrimaryEmail =>
        Emails.FirstOrDefault(e => e.Primary)?.Value
        ?? Emails.FirstOrDefault()?.Value
        ?? (UserName.Contains('@') ? UserName : null);
}

public class ScimName
{
    [JsonPropertyName("formatted")]
    public string? Formatted { get; set; }

    [JsonPropertyName("givenName")]
    public string? GivenName { get; set; }

    [JsonPropertyName("familyName")]
    public string? FamilyName { get; set; }

    /// <summary>The best available display name, preferring what the IdP formatted itself.</summary>
    public string? Best =>
        !string.IsNullOrWhiteSpace(Formatted) ? Formatted
        : string.Join(" ", new[] { GivenName, FamilyName }.Where(p => !string.IsNullOrWhiteSpace(p)));
}

public class ScimEmail
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

public class ScimGroupRef
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("display")]
    public string? Display { get; set; }
}

/// <summary>A SCIM group resource. Members are user ids.</summary>
public class ScimGroup
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = [ScimSchemas.Group];

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("externalId")]
    public string? ExternalId { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("members")]
    public List<ScimMember> Members { get; set; } = new();

    [JsonPropertyName("meta")]
    public ScimMeta? Meta { get; set; }
}

public class ScimMember
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("display")]
    public string? Display { get; set; }
}

public class ScimMeta
{
    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; } = "User";

    [JsonPropertyName("created")]
    public DateTime? Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime? LastModified { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }
}

/// <summary>A SCIM list response. <c>totalResults</c> is the count before paging, not after.</summary>
public class ScimListResponse<T>
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = [ScimSchemas.ListResponse];

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("startIndex")]
    public int StartIndex { get; set; } = 1;

    [JsonPropertyName("itemsPerPage")]
    public int ItemsPerPage { get; set; }

    [JsonPropertyName("Resources")]
    public List<T> Resources { get; set; } = new();
}

/// <summary>
/// A SCIM PATCH request (RFC 7644 §3.5.2).
///
/// PATCH semantics are where SCIM implementations usually go wrong, and where Entra ID and Okta both
/// insist on correctness: <c>replace</c> on <c>active</c> is how a deprovision arrives, and an
/// implementation that only handles <c>PUT</c> silently never disables anyone.
/// </summary>
public class ScimPatchRequest
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = [ScimSchemas.PatchOp];

    [JsonPropertyName("Operations")]
    public List<ScimPatchOperation> Operations { get; set; } = new();
}

public class ScimPatchOperation
{
    /// <summary><c>add</c>, <c>replace</c> or <c>remove</c>, case-insensitive per the RFC.</summary>
    [JsonPropertyName("op")]
    public string Op { get; set; } = string.Empty;

    /// <summary>Attribute path. Optional for <c>replace</c>, where the value is an object of attributes.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("value")]
    public System.Text.Json.JsonElement? Value { get; set; }
}

/// <summary>A SCIM error response.</summary>
public class ScimError
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = [ScimSchemas.Error];

    [JsonPropertyName("status")]
    public string Status { get; set; } = "400";

    /// <summary>SCIM error type — <c>invalidFilter</c>, <c>uniqueness</c>, <c>invalidValue</c>…</summary>
    [JsonPropertyName("scimType")]
    public string? ScimType { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    public static ScimError Create(int status, string detail, string? scimType = null) =>
        new() { Status = status.ToString(), Detail = detail, ScimType = scimType };
}

/// <summary>
/// A provisioning token as the admin UI sees it. The secret is present exactly once, on issue.
/// </summary>
public class ScimTokenView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public int? IdentityProviderId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Populated only by the issue call. Every read path leaves it null, which is what makes the
    /// credential write-only.
    /// </summary>
    public string? Secret { get; set; }
}
