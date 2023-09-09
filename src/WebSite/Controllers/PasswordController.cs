using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAL.Entities;
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

    private User GetLinkDataUser(string key)
    {
        try
        {
            var linkDataBytes = _linksService.GetLinkData("passwordReset", key);
            
            var linkData = JsonSerializer.Deserialize<PasswordResetLinkData>(linkDataBytes);
            
            if (linkData == null)
            {
                _logger.LogError("Link data is null");
                throw new DataNotFoundException("link", key, new Exception("Link data is null"));
            }

            var user = _usersService.GetUserById(linkData.UserId);
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
    public IActionResult ResetPassword(string key)
    {
        try
        {
            
            var user = GetLinkDataUser(key);
            
            var viewModel = new PasswordResetViewModel
            {
                Key = key,
                Username = Encoding.UTF8.GetString(user.Username),
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
    public IActionResult ResetPassword(PasswordResetViewModel viewModel)
    {
        if (!ModelState.IsValid)
        {
            return View(viewModel);
        }

        if(viewModel.NewPassword != viewModel.ConfirmPassword) 
        {
            ModelState.AddModelError("ConfirmPassword", "Passwords do not match");
            return View(viewModel);
        }

        try
        {
            var user = GetLinkDataUser(viewModel.Key);

            if (Encoding.UTF8.GetString(user.Username) != viewModel.Username)
            {
                _logger.LogError("User id does not match link data");
                ModelState.AddModelError("Error", "Inconsistent data");
                return View(viewModel);
            }
            
            if(user.Enabled == null || user.Enabled == false)
            {
                _logger.LogError("User is disabled");
                ModelState.AddModelError("Error", "Cannot reset disabled user's password");
                return View(viewModel);
            }

            if (!_usersService.CheckPasswordComplexity(viewModel.NewPassword))
            {
                _logger.LogError("Password does not meet complexity requirements");
                ModelState.AddModelError("Error", "Password must have at least 8 characters and contains a uppercase letter, a lowercase letter, a number and a special character");
                return View(viewModel);
            }

            _usersService.ChangePassword(user.Value, viewModel.NewPassword);

            // Links can only be used once so delete it
            _linksService.DeleteLink("passwordReset", viewModel.Key);
            
            // Redirect to a success page or appropriate action
            return RedirectToAction("ResetPasswordConfirmation");
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error resetting password message: {Message}", ex.Message);
            //ModelState.AddModelError("Error", "This link is invalid");
            Redirect("/Error");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

    }

    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }
}