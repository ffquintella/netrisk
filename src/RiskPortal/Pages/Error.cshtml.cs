using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RiskPortal.Pages;

/// <summary>
/// The unhandled-exception page.
///
/// It shows a request id and nothing else. A stack trace on an internet-facing page tells an attacker
/// which framework versions and which internal types are in play, and tells the reviewer nothing they
/// can act on.
/// </summary>
[AllowAnonymous]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ErrorModel : PageModel
{
    public string? RequestId { get; private set; }

    public void OnGet() => RequestId = HttpContext.TraceIdentifier;
}
