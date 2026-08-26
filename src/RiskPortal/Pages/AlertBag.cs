namespace RiskPortal.Pages;

/// <summary>
/// The two messages every page can show.
///
/// A type rather than two loose <c>TempData</c> keys so a page cannot spell one of them differently
/// and silently stop showing refusals — which is the failure mode that matters here, since a refusal
/// carries the reason the reviewer's decision did not stick.
/// </summary>
public class AlertBag
{
    public string? Error { get; set; }

    public string? Notice { get; set; }
}
