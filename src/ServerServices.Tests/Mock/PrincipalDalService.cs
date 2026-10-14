using System.Collections.Generic;
using System.Security.Claims;
using DAL.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ServerServices.Services;

namespace ServerServices.Tests.Mock;

/// <summary>
/// The production <see cref="DalService"/> with only the provider swapped for the in-memory one. Identity,
/// entity scope and audit actor are resolved exactly as in a request — from the principal the
/// <see cref="Accessor"/> carries — so a test acting as a scoped user exercises
/// <see cref="DalService.GetCurrentEntityScope"/> itself (T292), not a scope a test helper stated.
/// <see cref="InMemoryDalService"/> takes the scope as given and cannot see a defect in how it is derived.
/// </summary>
public sealed class PrincipalDalService : DalService
{
    private readonly string _databaseName;

    public PrincipalDalService(string databaseName, FixedHttpContextAccessor accessor)
        : base(Configuration(), accessor)
    {
        _databaseName = databaseName;
        Accessor = accessor;
    }

    public FixedHttpContextAccessor Accessor { get; }

    /// <summary>Acts as a principal carrying <paramref name="claims"/> (plus a name and the user id 1).</summary>
    public void ActAs(params Claim[] claims) => Accessor.ActAs(claims);

    protected override DbContextOptionsBuilder<NRDbContext> GetDbContextOptionsBuilder() =>
        new DbContextOptionsBuilder<NRDbContext>().UseInMemoryDatabase(_databaseName).EnableSensitiveDataLogging();

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "server=localhost;database=netrisk_tests"
            })
            .Build();
}

/// <summary>
/// An accessor whose context is a plain field, not the <c>AsyncLocal</c> of <see cref="HttpContextAccessor"/>,
/// so the principal a test sets in its constructor is the one every later call sees.
/// </summary>
public sealed class FixedHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }

    /// <summary>An authenticated principal named "scoped" with user id 1 and <paramref name="claims"/>.</summary>
    public void ActAs(params Claim[] claims)
    {
        var all = new List<Claim> { new(ClaimTypes.Name, "scoped"), new(ClaimTypes.Sid, "1") };
        all.AddRange(claims);
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(all, "Bearer")) };
    }

    /// <summary>No request at all: a migration or a tool that composes the DAL without a principal.</summary>
    public void ActAsNobody() => HttpContext = null;
}
