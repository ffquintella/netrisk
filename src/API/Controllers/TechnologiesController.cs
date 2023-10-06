using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class TechnologiesController: ApiBaseController
{
    
    private ITechnologiesService TechnologiesService { get; }
    
    public TechnologiesController(ILogger logger, 
        IHttpContextAccessor httpContextAccessor, IUsersService usersService, ITechnologiesService technologiesService) 
        : base(logger, httpContextAccessor, usersService)
    {
        TechnologiesService = technologiesService;
    }
    
    public List<Technology> GetAll()
    {
        return TechnologiesService.GetAll();
    }


}