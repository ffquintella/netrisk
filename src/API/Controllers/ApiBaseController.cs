﻿using System.Globalization;
using API.Exceptions;
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
        
        if (user.Lang == null)
        {
            user.Lang = "en";
            SetLocalization("en-US");
        }
        else
            SetLocalization(user.Lang);

        return (User)user;
    }
    
    protected async Task<User> GetUserAsync()
    {
        var userAccount =  UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);
        
        if (userAccount == null)
        {
            Logger.Error("Authenticated userAccount not found");
            throw new UserNotFoundException();
        }
        
        var user = await  UsersService.GetUserAsync(userAccount);
        if (user != null) return user;
        Logger.Error("Authenticated user not found");
        throw new UserNotFoundException();

    }
    
    protected void SetLocalization(string localization)
    {
        
        if (localization =="pt")
            localization = "pt-BR";
        else if(localization == "en")
            localization = "en-US";
        
        if(localization != "pt-BR" && localization != "en-US")
            throw new BadRequestException("Invalid localization");
        
        var culture = new CultureInfo(localization); // Set the culture 
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
    
}