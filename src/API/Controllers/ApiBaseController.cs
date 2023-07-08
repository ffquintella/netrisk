using API.Tools;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;


namespace API.Controllers;

public class ApiBaseController: ControllerBase
{
    protected ILogger Logger;
    protected readonly IHttpContextAccessor _httpContextAccessor;
    protected readonly IUserManagementService _userManagementService;
    
    public ApiBaseController(ILogger logger, 
        IHttpContextAccessor httpContextAccessor,
        IUserManagementService userManagementService)
    {
        Logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _userManagementService = userManagementService;
    }
    
    protected User GetUser()
    {
        var userAccount =  UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);
        
        if (userAccount == null)
        {
            Logger.Error("Authenticated userAccount not found");
            throw new UserNotFoundException();
        }
        
        var user = _userManagementService.GetUser(userAccount);
        if (user == null )
        {
            Logger.Error("Authenticated user not found");
            throw new UserNotFoundException();
        }

        return user;
    }
    
}