namespace GUIClient.Notifications;

/// <summary>
/// The app's transient-feedback channel, introduced for IX-4: routine successes are reported
/// with a toast that disappears on its own rather than a modal MessageBox the user has to
/// dismiss. Errors still get a modal box, because they need to be acknowledged.
/// </summary>
public interface INotificationService
{
    /// <summary>Reports a completed routine action ("Saved", "Deleted", …).</summary>
    void Success(string message);

    /// <summary>Reports neutral progress or context.</summary>
    void Info(string message);

    /// <summary>Reports something the user should notice but that did not fail.</summary>
    void Warning(string message);

    /// <summary>
    /// Reports a failure transiently. Use only where the user cannot act on it anyway; a failure
    /// the user must respond to still belongs in a modal error box per IX-4.
    /// </summary>
    void Error(string message);
}
