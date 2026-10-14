using System;
using System.Collections.Generic;
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
using ServerServices.Security;
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

    /// <summary>
    /// Opens a context scoped to <paramref name="entityScope"/> rather than to the calling principal — for work done on
    /// behalf of somebody who is not the caller. The job host's principal is unrestricted (T237); a job that renders or
    /// sends what a <i>user</i> configured reads through this, under that user's scope as
    /// <c>IUserEntityScopeResolver</c> resolves it at run time, so the user's schedule never sees more than the user
    /// could (T237 review, <c>ScheduledReportJob</c>). No audit identity: nothing is meant to be written through it.
    /// </summary>
    AuditableContext GetContextFor(EntityScope entityScope)
    {
        var context = GetContext(withIdentity: false, bypassEntityScope: true);
        context.EntityScope = entityScope;
        return context;
    }

    /// <summary>
    /// The entity tree (<c>entities.parent</c>) hierarchical scope expands claims through (T292). The production
    /// service serves it from the host's cache; this default loads it afresh, for doubles that open contexts on
    /// their own store.
    /// </summary>
    EntityTree GetEntityTree()
    {
        using var context = GetContext(withIdentity: false, bypassEntityScope: true);
        return EntityTree.Build(
            context.Entities.AsNoTracking().Select(e => new EntityTreeRow(e.Id, e.Parent)).ToList(), 0);
    }
}

public class DalService : IDalService
{
    // requires using Microsoft.Extensions.Configuration;
    private readonly string _connectionString;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IEntityHierarchy _entityHierarchy;
    private bool _enableSQLLogging = false;
    
    /// <param name="entityHierarchy">
    /// The host's shared tree cache (T292). Optional so a host or a test that composes the DAL by hand still gets
    /// hierarchical scope, over a cache of its own.
    /// </param>
    public DalService(IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IEntityHierarchy? entityHierarchy = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _entityHierarchy = entityHierarchy ?? new EntityHierarchy();
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
    
    /// <summary>
    /// The provider every context is opened on. Virtual only so a test can put the real
    /// <see cref="GetContext"/> — identity, entity scope and audit actor resolved from the real accessor —
    /// on an in-memory store (<c>BackgroundJobs.Tests</c>); no production type overrides it.
    /// </summary>
    protected virtual DbContextOptionsBuilder<NRDbContext> GetDbContextOptionsBuilder()
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
    
    public virtual AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false)
    {
        var optionsBuilder = GetDbContextOptionsBuilder();

        var dbContext = new AuditableContext(optionsBuilder.Options);
        
        dbContext.UserId = withIdentity ? GetUserId() : 0;
        dbContext.EntityScope = bypassEntityScope ? EntityScope.Unrestricted : GetCurrentEntityScope();

        // T292: a save that creates, deletes or re-parents an entity changes what every claim grants, so the
        // cached tree goes with it — whoever saved, scoped or not.
        dbContext.EntityTreeChanged = _entityHierarchy.Invalidate;

        // Track 8 milestone 8.4.1 — who the field-level trail attributes this save to. A context
        // opened without an HTTP principal is a job, the console or a migration, and says so rather
        // than writing an unattributed row.
        dbContext.AuditActor = ResolveAuditActor();
        
        return dbContext;
    }

    /// <summary>
    /// Derives the caller's entity scope from their claims.
    ///
    /// No authenticated principal means no principal to scope by — a migration or a tool that composes
    /// the DAL without an accessor principal — and that runs unrestricted. An authenticated principal
    /// with no entity assignment gets nothing rather than everything: the 2.3 spec is explicit that the
    /// default is deny. The job host and the console client do present an authenticated principal
    /// (<c>BackgroundServiceHttpContextAccessor</c>), so they are scoped like a user: the job host's
    /// carries <c>scope=global</c> and reads everything (T237, S39 D-01); the console's carries no
    /// scope claim and reads <c>DenyAll</c>.
    ///
    /// The scope is hierarchical (T292, S39 §10.4): each <c>entity_id</c> claim grants that entity and its
    /// descendants in <c>entities.parent</c>, never its ancestors or siblings, and never an entity linked to it
    /// only through an EAV property. The expansion happens here, once, against the host's cached
    /// <see cref="EntityTree"/>; every query filter and the write guard then read the expanded set, so nothing
    /// walks the tree per row. <c>EntityScopeHierarchyTest</c> covers every scoped record type.
    /// </summary>
    public EntityScope GetCurrentEntityScope()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext?.User.Identity is not { IsAuthenticated: true }) return EntityScope.Unrestricted;

        var user = httpContext.User;

        if (EntityScopeClaims.IsUnrestricted(user)) return EntityScope.Unrestricted;

        var claimed = EntityScopeClaims.ClaimedEntityIds(user);

        // No claim, no tree: an empty scope must stay DenyAll without a database round trip.
        if (claimed.Count == 0) return EntityScope.DenyAll;

        return GetEntityTree().ScopeFor(claimed);
    }

    /// <summary>
    /// The entity tree the scope expands through, from the host's cache: loaded on first use, after any save that
    /// changes the tree, and at most every <see cref="EntityHierarchy.DefaultTimeToLive"/> otherwise.
    /// </summary>
    public EntityTree GetEntityTree() => _entityHierarchy.GetTree(LoadEntityTree);

    /// <summary>
    /// One narrow query over <c>entities</c>, unscoped — the entity map carries no scope of its own and the tree
    /// must be whole to be expanded correctly. Never opens a scoped context, so it cannot recurse into
    /// <see cref="GetCurrentEntityScope"/>.
    /// </summary>
    private IReadOnlyCollection<EntityTreeRow> LoadEntityTree()
    {
        using var context = GetContext(withIdentity: false, bypassEntityScope: true);

        return context.Entities
            .AsNoTracking()
            .Select(e => new EntityTreeRow(e.Id, e.Parent))
            .ToList();
    }
}