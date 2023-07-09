namespace WebSite.Models;

using System.ComponentModel.DataAnnotations;
public class PasswordResetViewModel
{
    
    public string Key { get; set; } = "";
    public string Username { get; set; } = "";
    [Required] public int Id { get; set; } = 0;

    [Required]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = "";
}