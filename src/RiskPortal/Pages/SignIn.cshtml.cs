using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RiskPortal.Models;
using RiskPortal.Services;

namespace RiskPortal.Pages;

/// <summary>
/// Sign-in.
///
/// The portal has no user store of its own: it exchanges the reviewer's NetRisk credentials for an API
/// token and keeps the token. The password reaches <see cref="IPortalApiClient.SignInAsync"/> and goes
/// no further — not into the cookie, not into a session, not into a log.
/// </summary>
[AllowAnonymous]
public class SignInModel(IPortalApiClient api, ILogger<SignInModel> logger) : PageModel
{
    [BindProperty]
    public string Login { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public AlertBag Alerts { get; } = new();

    /// <summary>
    /// Null until checked. Non-null and unapproved means the sign-in form is pointless, so the page
    /// explains the one-time approval instead of collecting credentials that cannot work.
    /// </summary>
    public PortalRegistrationState? Registration { get; private set; }

    public async Task OnGetAsync()
    {
        Registration = await api.GetRegistrationStateAsync(HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Registration = await api.GetRegistrationStateAsync(HttpContext.RequestAborted);

        if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrWhiteSpace(Password))
        {
            Alerts.Error = "Enter your NetRisk username and password.";
            return Page();
        }

        var token = await api.SignInAsync(Login.Trim(), Password, HttpContext.RequestAborted);

        // Deliberately one message for both "no such account" and "wrong password". Distinguishing
        // them turns the sign-in form into an account-enumeration oracle.
        if (token is null)
        {
            logger.LogInformation("Portal sign-in refused");
            Alerts.Error = "That username and password combination was not accepted.";
            return Page();
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, Login.Trim()),
            new Claim(PortalSession.TokenClaim, token)
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });

        // Only local redirects. An open redirect on a sign-in page is a phishing primitive: the
        // attacker gets the victim to authenticate and then lands them somewhere else entirely.
        return Url.IsLocalUrl(ReturnUrl) ? Redirect(ReturnUrl!) : RedirectToPage("/Index");
    }
}
