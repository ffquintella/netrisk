using System;
using System.Linq;
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

public interface IDalService
{
    AuditableContext GetContext(bool withIdentity = true);
}

public class DalService : IDalService
{
    // requires using Microsoft.Extensions.Configuration;
    private readonly string _connectionString;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private bool _enableSQLLogging = false;
    
    public DalService(IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        _connectionString = configuration["Database:ConnectionString"]!;
        
        if (configuration["Database:EnableSQLLogging"] != null)
        {
            if (configuration["Database:EnableSQLLogging"] == null) _enableSQLLogging = false;
            else _enableSQLLogging = bool.Parse(configuration["Database:EnableSQLLogging"]!);
            
        }

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
        
        userAccount = userAccount.ToLower();
        
        using var context = GetContext(false);

        var user = context.Users.FirstOrDefault(usr =>  usr.Login.ToLower() == userAccount);
        
        if (user == null )
        {
            Log.Error("Authenticated user not found user:{UserAccount}", userAccount);
            throw new UserNotFoundException("User not found");
        }

        return user.Value;
    }

    public string GetConnectionString()
    {
        return _connectionString;
    }

    public ServerVersion GetMysqlServerVersion()
    {
        //return ServerVersion.Parse("8.0.29-mysql");
        return ServerVersion.AutoDetect(_connectionString);
        
    }
    
    private DbContextOptionsBuilder<NRDbContext> GetDbContextOptionsBuilder()
    {
        var optionsBuilder = new DbContextOptionsBuilder<NRDbContext>();
        
        optionsBuilder.UseMySql(_connectionString,
            GetMysqlServerVersion(),
            mysqlOptions =>
            {
                mysqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 15,
                    maxRetryDelay: TimeSpan.FromSeconds(60),
                    errorNumbersToAdd: null
                );
            });


        #if DEBUG

        if (_enableSQLLogging)
        {
            // DETAILED EF LOGGING
            optionsBuilder.EnableDetailedErrors();
            optionsBuilder.EnableSensitiveDataLogging();
            //optionsBuilder.LogTo(Console.WriteLine);
            optionsBuilder.LogTo(s => Log.Debug(s));
        }

        #endif
        
        return optionsBuilder;
    }
    
    public AuditableContext GetContext(bool withIdentity = true)
    {
        var optionsBuilder = GetDbContextOptionsBuilder();

        var dbContext = new AuditableContext(optionsBuilder.Options);
        
        dbContext.UserId = withIdentity ? GetUserId() : 0;
        
        return dbContext;
    }
}