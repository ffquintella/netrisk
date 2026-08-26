using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RiskPortal.Services;

namespace RiskPortal.Pages;

/// <summary>
/// Sign-out.
///
/// POST only, and antiforgery-protected: a GET sign-out can be triggered by any image tag on any
/// page, which is a nuisance rather than a vulnerability but an avoidable one.
///
/// It revokes the API token server-side before dropping the cookie (finding NR-2026-028). Dropping
/// the cookie alone would leave a token valid for the rest of its hour — which is exactly the gap
/// that finding was about.
/// </summary>
public class SignOutModel(IPortalApiClient api, IPortalSession session) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        var token = session.Token;

        if (token is not null) await api.SignOutAsync(token, HttpContext.RequestAborted);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToPage("/SignIn");
    }
}
