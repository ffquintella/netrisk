using Microsoft.AspNetCore.Mvc;
using WebSite.Models;

namespace WebSite.Controllers;

public class PasswordController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public PasswordController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }
    
   
    [HttpGet]
    public IActionResult ResetPassword(string email)
    {
        var viewModel = new PasswordResetViewModel
        {
            Email = email
        };
        return View(viewModel);
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