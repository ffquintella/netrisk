using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Users;
using ServerServices.Interfaces;
using WebSite.Models;

namespace WebSite.Controllers;

public class PasswordController : Controller
{
    private readonly ILogger<PasswordController> _logger;
    private readonly ILinksService _linksService;
    private readonly IUsersService _usersService;

    public PasswordController(ILogger<PasswordController> logger,
        ILinksService linksService,
        IUsersService usersService)
    {
        _logger = logger;
        _linksService = linksService;
        _usersService = usersService;
    }
    
   
    [HttpGet]
    public IActionResult ResetPassword(string key)
    {
        try
        {
            var linkDataBytes = _linksService.GetLinkData("passwordReset", key);
            
            var linkData = JsonSerializer.Deserialize<PasswordResetLinkData>(linkDataBytes);
            
            if (linkData == null)
            {
                _logger.LogError("Link data is null");
                return NotFound();
            }

            var user = _usersService.GetUserById(linkData.UserId);
            if (user == null)
            {
                _logger.LogError("User not found");
                return NotFound();
            }
            
            var viewModel = new PasswordResetViewModel
            {
                Key = key,
                Id = user.Value,
                Username = Encoding.UTF8.GetString(user.Username),
            };
            return View(viewModel);
        }
        catch (DataNotFoundException ex)
        {
            _logger.LogError("Link not found");
            return NotFound();
        }
    }


    [HttpPost]
    public IActionResult ResetPassword(PasswordResetViewModel viewModel)
    {
        if (!ModelState.IsValid)
        {
            return View(viewModel);
        }

        // TODO: Implement your password reset logic here

        // Redirect to a success page or appropriate action
        return RedirectToAction("ResetPasswordConfirmation");
    }

    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }
}