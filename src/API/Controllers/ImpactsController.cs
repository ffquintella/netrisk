using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Globalization;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class ImpactsController : ApiBaseController
{
    private IImpactsService ImpactsService { get; }
    public ImpactsController(ILogger logger, 
        IHttpContextAccessor httpContextAccessor, 
        IUsersService usersService,
        IImpactsService impactsService)
        : base(logger, httpContextAccessor, usersService)
    {
        ImpactsService = impactsService;
    }
    
    public List<LocalizableListItem> GetAll()
    {
        return ImpactsService.GetAll();
    }
    
}