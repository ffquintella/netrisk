using Microsoft.AspNetCore.Mvc.Rendering;
using WebSite.Tools;

namespace WebSite.ExtensionMethods;

public static class HtmlHelperExtensionMethods
{
    public static string Translate(this IHtmlHelper helper, string key)
    {
        var services = helper.ViewContext.HttpContext.RequestServices;
        var localizer = services.GetRequiredService<LanguageService>();
        string result = localizer[key];
        return result;
    }
}