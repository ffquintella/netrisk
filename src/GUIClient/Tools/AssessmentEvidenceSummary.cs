using Model.Assessments;

namespace GUIClient.Tools;

/// <summary>
/// The pure half of the run viewer's comment and evidence controls (GitHub #80, T297, S44): when the
/// controls are offered, which message a picked file is refused with before anything is uploaded, and
/// which message a refusal from the server reads as.
///
/// No Avalonia types, so <c>GUIClient.Tests</c> compiles this file directly — that project
/// deliberately does not reference <c>GUIClient</c>.
/// </summary>
public static class AssessmentEvidenceSummary
{
    public const string EmptyKey = "EvidenceEmptyMSG";
    public const string TooLargeKey = "EvidenceTooLargeMSG";
    public const string LimitReachedKey = "EvidenceLimitReachedMSG";
    public const string RunSubmittedKey = "EvidenceRunSubmittedMSG";
    public const string UploadFailedKey = "ErrorUploadingFileMSG";
    public const string NotUploaderKey = "EvidenceNotUploaderMSG";
    public const string DeleteFailedKey = "FileDeletionErrorMSG";
    public const string DownloadFailedKey = "ErrorDownloadingFileMSG";

    /// <summary>
    /// Every message key this file hands to the localizer. They are computed, so the coverage test that
    /// scans the source for literal localizer lookups cannot see them; <c>AssessmentEvidenceSummaryTest</c> resolves each
    /// one in all three resource files instead.
    /// </summary>
    public static readonly string[] MessageKeys =
    [
        EmptyKey, TooLargeKey, LimitReachedKey, RunSubmittedKey, UploadFailedKey, NotUploaderKey,
        DeleteFailedKey, DownloadFailedKey
    ];

    /// <summary>
    /// Evidence can be attached only to a real, open run: a preview has no run to attach to, and a
    /// submitted run is read-only.
    /// </summary>
    public static bool CanAttach(bool isPreview, bool isReadOnly) => !isPreview && !isReadOnly;

    /// <summary>
    /// A comment can be typed on anything but a submitted run. In a preview it is not saved, which is
    /// what a preview is for — seeing the form as an assessor will.
    /// </summary>
    public static bool CanEditComment(bool isReadOnly) => !isReadOnly;

    /// <summary>
    /// The localization key a picked file is refused with before it is uploaded, or null when it may be
    /// uploaded. The server enforces the same <see cref="AssessmentEvidencePolicy"/>; this only spares the
    /// assessor an upload that would be refused at the end.
    /// </summary>
    public static string? UploadRejectionKey(long fileBytes, int attachedCount)
    {
        if (attachedCount >= AssessmentEvidencePolicy.MaxEvidencePerAnswer) return LimitReachedKey;
        if (fileBytes <= 0) return EmptyKey;
        if (fileBytes > AssessmentEvidencePolicy.MaxEvidenceBytes) return TooLargeKey;
        return null;
    }

    /// <summary>
    /// The localization key for a refusal from the server, read from the rule name the API puts in the
    /// body of a 409 (<c>run_submitted</c>, <c>evidence_limit_reached</c>); anything else is the generic
    /// upload failure.
    /// </summary>
    public static string FailureKey(string? serverMessage)
    {
        if (string.IsNullOrEmpty(serverMessage)) return UploadFailedKey;
        if (serverMessage.Contains("run_submitted", System.StringComparison.Ordinal)) return RunSubmittedKey;
        if (serverMessage.Contains("evidence_limit_reached", System.StringComparison.Ordinal)) return LimitReachedKey;
        return UploadFailedKey;
    }

    /// <summary>
    /// The localization key for a refused delete: somebody else's evidence (403 <c>not_the_uploader</c>),
    /// a submitted run, or the generic failure.
    /// </summary>
    public static string DeleteFailureKey(string? serverMessage)
    {
        if (string.IsNullOrEmpty(serverMessage)) return DeleteFailedKey;
        if (serverMessage.Contains("not_the_uploader", System.StringComparison.Ordinal)) return NotUploaderKey;
        if (serverMessage.Contains("run_submitted", System.StringComparison.Ordinal)) return RunSubmittedKey;
        return DeleteFailedKey;
    }

    /// <summary>
    /// The MIME type the file icon is chosen by. The icon converter refuses an empty one, and a listing
    /// whose type row was removed would otherwise arrive without any.
    /// </summary>
    public static string IconType(string? type) =>
        string.IsNullOrWhiteSpace(type) ? "application/octet-stream" : type;
}
