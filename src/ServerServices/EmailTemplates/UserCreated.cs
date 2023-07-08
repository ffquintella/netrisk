using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ServerServices.EmailTemplates;

public class UserCreated : PageModel
{
    public string Name { get; set; } = "";
    public string Link { get; set; } = "";
    
    public void OnGet()
    {
        
    }
}