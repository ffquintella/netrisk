using DAL.Entities;
using DAL.Enums;
using Model.Integrations;

namespace ServerServices.Interfaces;

/// <summary>
/// One issue tracker's API, behind a common contract (Track 4 milestone 4.2.1).
///
/// A provider does transport and shape translation and nothing else: no severity mapping, no status
/// interpretation, no decision about whether to create an issue. Those are per-connection policy and
/// live in the service, which is what lets a fifth tracker be added without touching any of it.
///
/// Every method takes the connection with its token already decrypted. A provider never holds the
/// key and never writes to the database.
/// </summary>
public interface IIssueTrackerProvider
{
    IssueTrackerProviderKind Kind { get; }

    string Name { get; }

    IssueTrackerCapabilities Capabilities { get; }

    /// <summary>
    /// Verifies credentials and that the configured project exists. Both, because a valid token
    /// against a mistyped project key is the failure an operator is most likely to create.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(IssueTrackerConnection connection, string? token,
        CancellationToken ct = default);

    Task<ExternalIssue> CreateIssueAsync(IssueTrackerConnection connection, string? token,
        IssueDraft draft, CancellationToken ct = default);

    /// <summary>
    /// Posts a comment and, when <paramref name="transitionTo"/> is given and the tracker supports
    /// it, moves the issue. Returns the issue as it stands afterwards.
    /// </summary>
    Task<ExternalIssue> UpdateIssueAsync(IssueTrackerConnection connection, string? token,
        string issueKey, string? comment, string? transitionTo, CancellationToken ct = default);

    /// <summary>Reads an issue's current state. The polling fallback's only call.</summary>
    Task<ExternalIssue?> GetIssueAsync(IssueTrackerConnection connection, string? token,
        string issueKey, CancellationToken ct = default);

    /// <summary>
    /// Validates an inbound webhook and extracts the issue it is about. Returns null when the
    /// signature does not verify or the payload is not an issue event — the caller answers 401 or 204
    /// respectively, and never acts on an unverified body.
    /// </summary>
    ExternalIssue? ParseWebhook(IssueTrackerConnection connection, string? webhookSecret,
        string rawBody, IReadOnlyDictionary<string, string> headers);
}

/// <summary>Resolves the provider for a connection's tracker kind (Track 4 milestone 4.2.1).</summary>
public interface IIssueTrackerProviderRegistry
{
    IReadOnlyList<IIssueTrackerProvider> All { get; }

    IIssueTrackerProvider? For(IssueTrackerProviderKind kind);
}
