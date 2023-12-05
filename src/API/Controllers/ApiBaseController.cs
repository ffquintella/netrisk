using API.Tools;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices;
using ServerServices.Interfaces;
using Tools.User;
using ILogger = Serilog.ILogger;


namespace API.Controllers;

public class ApiBaseController: ControllerBase
{
    protected ILogger Logger;
    protected readonly IHttpContextAccessor _httpContextAccessor;
    protected readonly IUsersService UsersService;
    
    public ApiBaseController(ILogger logger, 
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService)
    {
        Logger = logger;
        _httpContextAccessor = httpContextAccessor;
        UsersService = usersService;
    }
    
    protected User GetUser()
    {
        var userAccount =  UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);
        
        if (userAccount == null)
        {
            Logger.Error("Authenticated userAccount not found");
            throw new UserNotFoundException();
        }
        
        var user = UsersService.GetUser(userAccount);
        if (user == null )
        {
            Logger.Error("Authenticated user not found");
            throw new UserNotFoundException();
        }

        return user;
    }
    
}