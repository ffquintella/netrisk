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
    /// <summary>
    /// Opens a context scoped to the calling principal's business entities
    /// (Track 2 milestone 2.3.1/2.3.2).
    /// </summary>
    /// <param name="withIdentity">Attribute audit rows to the calling user.</param>
    /// <param name="bypassEntityScope">
    /// Opens the context unfiltered. Only for operations that are legitimately organisation-wide
    /// and already gated by an admin-only policy — the Master Dashboard's rollup, schema upgrade
    /// tooling, and the authentication handlers that must read a user's assignments before any
    /// scope exists. Every other caller must leave this false.
    /// </param>
    AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false);

    /// <summary>The scope the current caller would get, for services that need to check a write.</summary>
    EntityScope GetCurrentEntityScope();
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

    /// <summary>
    /// The name recorded in <c>audit_logs.actor</c>. The login when there is one, the process name
    /// otherwise — never empty, because "attributable end to end" is the requirement.
    /// </summary>
    private string ResolveAuditActor()
    {
        var identity = _httpContextAccessor.HttpContext?.User.Identity;
        if (identity is { IsAuthenticated: true })
        {
            var name = Tools.User.UserHelper.GetUserName(identity);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }

        return AuditableContext.SystemActor;
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
    
    public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false)
    {
        var optionsBuilder = GetDbContextOptionsBuilder();

        var dbContext = new AuditableContext(optionsBuilder.Options);
        
        dbContext.UserId = withIdentity ? GetUserId() : 0;
        dbContext.EntityScope = bypassEntityScope ? EntityScope.Unrestricted : GetCurrentEntityScope();

        // Track 8 milestone 8.4.1 — who the field-level trail attributes this save to. A context
        // opened without an HTTP principal is a job, the console or a migration, and says so rather
        // than writing an unattributed row.
        dbContext.AuditActor = ResolveAuditActor();
        
        return dbContext;
    }

    /// <summary>
    /// Derives the caller's entity scope from their claims.
    ///
    /// No HTTP context means no principal to scope by — a background job, the console client or a
    /// migration — and those run unrestricted, as they always have. An authenticated user with no
    /// entity assignment gets nothing rather than everything: the 2.3 spec is explicit that the
    /// default is deny.
    /// </summary>
    public EntityScope GetCurrentEntityScope()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext?.User.Identity is not { IsAuthenticated: true }) return EntityScope.Unrestricted;

        var user = httpContext.User;

        if (user.HasClaim("scope", "global") || user.IsInRole("Admin")) return EntityScope.Unrestricted;

        var entityIds = user.Claims
            .Where(c => c.Type == "entity_id")
            .Select(c => int.TryParse(c.Value, out var id) ? (int?)id : null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value);

        return EntityScope.ForEntities(entityIds);
    }
}