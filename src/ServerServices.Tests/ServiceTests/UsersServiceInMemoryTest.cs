using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(UsersService))]
public class UsersServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IUsersService _svc;

    public UsersServiceInMemoryTest()
    {
        _svc = GetService<IUsersService>();
    }

    private static User NewUser(int value, string login = "alice", string name = "Alice",
        string type = "local", bool enabled = true, sbyte lockout = 0, string password = "secret") => new()
    {
        Value = value,
        Login = login,
        Name = name,
        Type = type,
        Enabled = enabled,
        Lockout = lockout,
        Email = $"{login}@x.io",
        Password = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(password))
    };

    [Fact]
    public void TestGetUserByLoginAndId()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "bob")));

        Assert.NotNull(_svc.GetUser("bob"));
        Assert.NotNull(_svc.GetUserById(1));
        Assert.Null(_svc.GetUser("nobody"));
    }

    [Fact]
    public async Task TestGetUserAsyncAndGetAll()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "u1"));
            ctx.Users.Add(NewUser(2, "u2"));
        });

        Assert.NotNull(await _svc.GetUserAsync("u1"));
        Assert.Equal(2, (await _svc.GetAllAsync()).Count);
        Assert.NotNull(await _svc.GetUserByIdAsync(1));
    }

    [Fact]
    public void TestVerifyPassword()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "carol", password: "pw123")));

        Assert.True(_svc.VerifyPassword("carol", "pw123"));
        Assert.False(_svc.VerifyPassword("carol", "wrong"));
        Assert.True(_svc.VerifyPassword(1, "pw123"));
        Assert.False(_svc.VerifyPassword((User?)null, "pw123"));
    }

    [Fact]
    public void TestVerifyPasswordLockedOutAndSaml()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "locked", lockout: 1));
            ctx.Users.Add(NewUser(2, "samluser", type: "saml"));
        });

        Assert.False(_svc.VerifyPassword("locked", "secret"));
        Assert.Throws<Exception>(() => _svc.VerifyPassword("samluser", "secret"));
    }

    [Fact]
    public async Task TestRegisterLogin()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "dave")));

        await _svc.RegisterLoginAsync(1, "127.0.0.1");

        Assert.NotNull(_svc.GetUserById(1)!.LastLogin);
        // unknown user is a no-op
        await _svc.RegisterLoginAsync(999, "127.0.0.1");
    }

    [Fact]
    public async Task TestFindEnabledActiveUser()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "active", name: "ActiveUser"));
            ctx.Users.Add(NewUser(2, "disabled", enabled: false));
        });

        Assert.NotNull(await _svc.FindEnabledActiveUserAsync("active"));
        Assert.Null(await _svc.FindEnabledActiveUserAsync("disabled"));
        Assert.NotNull(await _svc.FindEnabledActiveUserByNameAsync("ActiveUser"));
    }

    [Fact]
    public void TestSaveUser()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "eve", name: "Before")));

        var update = NewUser(1, "eve", name: "After");
        _svc.SaveUser(update);

        Assert.Equal("After", _svc.GetUserById(1)!.Name);
        Assert.Throws<DataNotFoundException>(() => _svc.SaveUser(NewUser(99, "x")));
    }

    [Fact]
    public void TestGetUserName()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "frank", name: "Frank")));

        Assert.Equal("Frank", _svc.GetUserName(1));
        Assert.Throws<DataNotFoundException>(() => _svc.GetUserName(999));
    }

    [Fact]
    public void TestDeleteUser()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "grace")));

        _svc.DeleteUser(1);

        Assert.Null(_svc.GetUserById(1));
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteUser(1));
    }

    [Fact]
    public async Task TestListActiveUsers()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "h1"));
            ctx.Users.Add(NewUser(2, "h2", enabled: false));
        });

        var list = await _svc.ListActiveUsersAsync();

        Assert.Single(list);
        Assert.Equal("h1", list[0].Username);
    }

    [Fact]
    public async Task TestGetByTeamId()
    {
        Seed(ctx =>
        {
            var user = NewUser(1, "teamed");
            user.Teams.Add(new Team { Value = 5, Name = "Team5" });
            ctx.Users.Add(user);
            ctx.Users.Add(NewUser(2, "loner"));
        });

        var members = await _svc.GetByTeamIdAsync(5);

        Assert.Single(members);
    }

    [Fact]
    public void TestCreateUser()
    {
        var created = _svc.CreateUser(NewUser(0, "Ivan"));

        Assert.Equal("ivan", created.Login);   // lowercased
        Assert.Throws<DataAlreadyExistsException>(() =>
        {
            Seed(ctx => ctx.Users.Add(NewUser(50, "dup")));
            _svc.CreateUser(NewUser(50, "dup"));
        });
    }

    [Fact]
    public void TestGetUserPermissions()
    {
        Seed(ctx =>
        {
            var user = NewUser(1, "perm");
            user.Permissions.Add(new Permission { Id = 1, Key = "do_things", Name = "Do Things", Description = "" });
            ctx.Users.Add(user);
        });

        var permissions = _svc.GetUserPermissions(1);

        Assert.Contains("do_things", permissions);
        Assert.Throws<UserNotFoundException>(() => _svc.GetUserPermissions(999));
    }

    [Fact]
    public void TestChangePassword()
    {
        Seed(ctx => ctx.Users.Add(NewUser(1, "jane", password: "old")));

        var ok = _svc.ChangePassword(1, "newpassword");

        Assert.True(ok);
        Assert.True(_svc.VerifyPassword(1, "newpassword"));
    }

    [Theory]
    [InlineData("short", false)]
    [InlineData("Str0ng!Passw0rd", true)]
    public void TestCheckPasswordComplexity(string password, bool expected)
    {
        Assert.Equal(expected, _svc.CheckPasswordComplexity(password));
    }
}
