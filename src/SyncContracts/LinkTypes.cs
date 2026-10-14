namespace SyncContracts;

/// <summary>
/// The <c>links.type</c> values the API pushes to the website on the fast lane. Declared here, once,
/// because the API writes the rows and the website filters on the type, and a spelling difference
/// between the two would make every link of that kind silently fail to resolve.
/// </summary>
public static class SyncLinkTypes
{
    public const string PasswordReset = "passwordReset";

    /// <inheritdoc cref="IrpTaskReportLink"/>
    public const string IrpTaskReport = IrpTaskReportLink.Type;

    /// <summary>Every type <c>SyncPushBuilder.BuildFastAsync</c> pushes.</summary>
    public static readonly IReadOnlyList<string> Pushed = [PasswordReset, IrpTaskReport];
}

/// <summary>
/// The link an incident-response task assignee receives by e-mail to report the task's outcome on the
/// public website (NR-2026-037, T340).
///
/// The website used to take the task-execution id itself as the key, so anyone could walk the
/// sequential ids, read every open incident's tasks, and mark any of them completed, skipped or
/// failed without a session. The key is now an unguessable per-execution token: the API draws it
/// from <c>Tools.RandomGenerator.RandomToken</c> (<see cref="KeyBytes"/> bytes of CSPRNG output),
/// stores only its SHA-256 (<c>Tools.Security.LinkKeyHash.Primary</c>) in the <c>links</c> table with
/// the execution id as the link's data, and the website resolves the execution only through it.
/// </summary>
public static class IrpTaskReportLink
{
    public const string Type = "irpTaskReport";

    /// <summary>Entropy of the key: 256 bits.</summary>
    public const int KeyBytes = 32;

    /// <summary>Length of the key as it appears in the URL: unpadded base64url of <see cref="KeyBytes"/>.</summary>
    public const int KeyLength = 43;

    /// <summary>How long an e-mailed report link stays usable if the task is never reported.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// True when <paramref name="key"/> has the exact shape the API issues. Checked before any lookup,
    /// so the website never hashes or queries on arbitrary-length input, and an integer id — the old
    /// key — is refused outright.
    /// </summary>
    public static bool IsWellFormedKey(string? key)
    {
        if (key is not { Length: KeyLength }) return false;
        foreach (var c in key)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')) return false;
        }
        return true;
    }
}

/// <summary>The JSON a <see cref="IrpTaskReportLink"/> row carries in <c>links.data</c>.</summary>
public class IrpTaskReportLinkData
{
    public int TaskExecutionId { get; set; }
}
