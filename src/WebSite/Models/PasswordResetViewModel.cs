namespace WebSite.Models;

using System.ComponentModel.DataAnnotations;
public class PasswordResetViewModel
{
    [Required] [EmailAddress] public string Email { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = "";
}