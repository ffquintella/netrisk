using System.Security.Claims;
using System.Text;
using DAL.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Model.Exceptions;
using Serilog;
using Serilog.Core;
using Tools.User;

namespace ServerServices.Services;

public class DALService
{
    // requires using Microsoft.Extensions.Configuration;
    private readonly IConfiguration Configuration;
    private string ConnectionString;
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public DALService(IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        Configuration = configuration;
        ConnectionString = Configuration["Database:ConnectionString"]!;

    }

    private int GetUserId()
    {

        if (_httpContextAccessor.HttpContext == null) return 0;
        
        if(_httpContextAccessor.HttpContext!.User.Identity == null) return 0;
        if(_httpContextAccessor.HttpContext!.User.Identity.Name == null) return 0;

        var sid = _httpContextAccessor.HttpContext!.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Sid);
        if( sid != null) return Int32.Parse(sid.Value);
        
        var userAccount =  UserHelper.GetUserName(_httpContextAccessor.HttpContext!.User.Identity);

        if (userAccount == null) return 0;
        
        using var context = GetContext(false);

        var user = context.Users.FirstOrDefault(usr => Encoding.UTF8.GetBytes(userAccount)  == usr.Username);
        
        //var user = UsersService.GetUser(userAccount);
        if (user == null )
        {
            Log.Error("Authenticated user not found");
            throw new UserNotFoundException();
        }

        return user.Value;
    }
    
    
    public AuditableContext GetContext(bool withIdentity = true)
    {
        //var optionsBuilder = new DbContextOptionsBuilder<NRDbContext>();
        var optionsBuilder = new DbContextOptionsBuilder<NRDbContext>();
        optionsBuilder.UseMySql(ConnectionString, Microsoft.EntityFrameworkCore.ServerVersion.Parse("8.0.29-mysql"));

        optionsBuilder.EnableDetailedErrors();
        optionsBuilder.LogTo(Console.WriteLine);

        var dbContext = new AuditableContext(optionsBuilder.Options);
        
        dbContext.UserId = withIdentity ? GetUserId() : 0;
        
        return dbContext;
    }
}