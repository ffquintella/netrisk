using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using ServerServices.Interfaces;
using WebSite.Tools;

namespace WebSite.Models;

public class DoFixReportViewModel
{


    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Solution { get; set; } = "";
    public string Score { get; set; } = "";
    public string HostName { get; set; } = "";
    public string FixerId { get; set; } = "";
    public bool IsTeamFix { get; set; } = false;
    public string FixerEmail { get; set; } = "";
    public DateOnly FixDate { get; set; } = DateOnly.MinValue;
    public List<SelectListItem> Fixers { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> Answers { get; set; } = new List<SelectListItem>();
    public string Comment { get; set; } = "";
    

}