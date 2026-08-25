namespace DAL.Entities;

/// <summary>
/// Audit row for one SCIM request (Track 4 milestone 4.3.2).
///
/// Full request auditing is part of the milestone rather than an extra: a provisioning connection can
/// disable every account in the product, and "when did the IdP deactivate this user, and did we
/// acknowledge it" is a question that gets asked during incidents, not during development.
/// </summary>
public class ScimRequestLog
{
    public int Id { get; set; }

    public int? TokenId { get; set; }

    public string Method { get; set; } = null!;

    public string Path { get; set; } = null!;

    public int StatusCode { get; set; }

    /// <summary>SCIM resource acted on — <c>userName</c> or a group name. Never a credential.</summary>
    public string? Target { get; set; }

    /// <summary>Short description of the effect: "created", "deactivated", "patched active=false".</summary>
    public string? Outcome { get; set; }

    public DateTime OccurredAt { get; set; }

    public virtual ScimToken? Token { get; set; }
}
