using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Users;
using WebSite.Models;
using WebSiteData.Entities;
using WebSiteData.Services;

namespace WebSite.Controllers;

public class PasswordController : Controller
{
    private readonly ILogger<PasswordController> _logger;
    private readonly ILocalLinkService _linkService;
    private readonly ILocalUserService _userService;

    public PasswordController(ILogger<PasswordController> logger,
        ILocalLinkService linkService,
        ILocalUserService userService)
    {
        _logger = logger;
        _linkService = linkService;
        _userService = userService;
    }

    private async Task<LocalUser> GetLinkDataUser(string key)
    {
        try
        {
            var linkDataJson = await _linkService.GetLinkDataAsync("passwordReset", key);
            if (linkDataJson == null)
            {
                _logger.LogError("Link data is null");
                throw new DataNotFoundException("link", key, new Exception("Link data is null"));
            }

            var linkData = JsonSerializer.Deserialize<PasswordResetLinkData>(linkDataJson);
            if (linkData == null)
            {
                _logger.LogError("Link data is null");
                throw new DataNotFoundException("link", key, new Exception("Link data is null"));
            }

            var user = await _userService.GetByIdAsync(linkData.UserId);
            if (user == null)
            {
                _logger.LogError("User not found");
                throw new DataNotFoundException("link", key, new Exception("User not found"));
            }
            return user;
        }
        catch (DataNotFoundException ex)
        {
            _logger.LogError("Unexpected error locating link");
            throw new DataNotFoundException("link", key, ex);
        }
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string key)
    {
        try
        {
            var user = await GetLinkDataUser(key);

            var viewModel = new PasswordResetViewModel
            {
                Key = key,
                Username = user.Login,
            };
            return View(viewModel);
        }
        catch (DataNotFoundException ex)
        {
            _logger.LogError("Link not found message:{Message}", ex.Message);
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(PasswordResetViewModel viewModel)
    {
        if (!ModelState.IsValid)
        {
            return View(viewModel);
        }

        if (viewModel.NewPassword != viewModel.ConfirmPassword)
        {
            ModelState.AddModelError("ConfirmPassword", "Passwords do not match");
            return View(viewModel);
        }

        try
        {
            var user = await GetLinkDataUser(viewModel.Key);

            if (user.Login.ToLower() != viewModel.Username.ToLower())
            {
                _logger.LogError("User id does not match link data");
                ModelState.AddModelError("Error", "Inconsistent data");
                return View(viewModel);
            }

            if (user.Enabled == null || user.Enabled == false)
            {
                _logger.LogError("User is disabled");
                ModelState.AddModelError("Error", "Cannot reset disabled user's password");
                return View(viewModel);
            }

            if (!_userService.CheckPasswordComplexity(viewModel.NewPassword))
            {
                _logger.LogError("Password does not meet complexity requirements");
                ModelState.AddModelError("Error", "Password must have at least 8 characters and contains a uppercase letter, a lowercase letter, a number and a special character");
                return View(viewModel);
            }

            // Queue the change (applied to the main DB by the next signed sync) and consume the link.
            await _userService.QueuePasswordChangeAsync(user.Value, viewModel.NewPassword);
            await _linkService.QueueLinkDeleteAsync("passwordReset", viewModel.Key);

            return RedirectToAction("ResetPasswordConfirmation");
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error resetting password message: {Message}", ex.Message);
            Redirect("/Error");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }
}
