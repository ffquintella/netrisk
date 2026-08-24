using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Exceptions;
using Model.Globalization;
using Model.Users;
using NSubstitute;
using ServerServices.Interfaces;
using SharedServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(UsersController))]
public class UsersControllerTest : BaseControllerTest
{
    private readonly IUsersService _usersService = Substitute.For<IUsersService>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly ILinksService _linksService = Substitute.For<ILinksService>();
    private readonly ILanguageManager _languageManager = Substitute.For<ILanguageManager>();
    private readonly IPermissionsService _permissionsService = Substitute.For<IPermissionsService>();
    private readonly ITeamsService _teamsService = Substitute.For<ITeamsService>();

    private readonly IUsersService _nonAdminUsersService = Substitute.For<IUsersService>();

    private readonly UsersController _controller;

    /// <summary>
    /// Same controller, but the authenticated caller is not an admin. Needed for the
    /// self-service branches of ChangePassword.
    /// </summary>
    private readonly UsersController _nonAdminController;

    public UsersControllerTest()
    {
        var loggedUser = MakeUser(1, "testUser", true);
        _usersService.GetUser("testUser").Returns(loggedUser);
        _usersService.GetUserAsync("testUser").Returns(loggedUser);

        var nonAdmin = MakeUser(1, "testUser", false);
        _nonAdminUsersService.GetUser("testUser").Returns(nonAdmin);
        _nonAdminUsersService.GetUserAsync("testUser").Returns(nonAdmin);

        _languageManager.DefaultLanguage.Returns(new LanguageModel("English", "English", "EN"));
        _languageManager.AllLanguages.Returns(new List<LanguageModel>
        {
            new LanguageModel("English", "English", "en"),
            new LanguageModel("Portuguese", "Português", "pt")
        });

        _linksService.CreateLink(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<byte[]>())
            .Returns("https://localhost/passwordReset/abc");

        _controller = ResolveController<UsersController>(s =>
        {
            s.AddSingleton(_usersService);
            s.AddSingleton(_emailService);
            s.AddSingleton(_linksService);
            s.AddSingleton(_languageManager);
            s.AddSingleton(_permissionsService);
            s.AddSingleton(_teamsService);
            s.AddSingleton<IConfiguration>(Configuration());
        });

        _nonAdminController = ResolveController<UsersController>(s =>
        {
            s.AddSingleton(_nonAdminUsersService);
            s.AddSingleton(_emailService);
            s.AddSingleton(_linksService);
            s.AddSingleton(_languageManager);
            s.AddSingleton(_permissionsService);
            s.AddSingleton(_teamsService);
            s.AddSingleton<IConfiguration>(Configuration());
        });
    }

    private static IConfiguration Configuration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["links:passwordResetDuration"] = "30"
            })
            .Build();
    }

    private static User MakeUser(int value, string login, bool admin)
    {
        return new User
        {
            Value = value,
            Login = login,
            Name = "Test User",
            Email = "testUser@teste.com",
            Type = "local",
            Lang = "en",
            Admin = admin,
            Enabled = true,
            Password = "secret"u8.ToArray(),
            Salt = "salt"
        };
    }

    #region GetUser

    [Fact]
    public void TestGetUser()
    {
        _usersService.GetUserById(2).Returns(MakeUser(2, "other", false));

        var result = _controller.GetUser(2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserDto>(ok.Value);
        Assert.Equal("Test User", dto.Name);
        Assert.Equal("testUser@teste.com", dto.Email);
    }

    [Fact]
    public void TestGetUserReturnsInternalErrorWhenUserIsNull()
    {
        var result = _controller.GetUser(50);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    [Fact]
    public void TestGetUserNotFound()
    {
        _usersService.GetUserById(999).Returns(_ => throw new DataNotFoundException("users", "999"));

        var result = _controller.GetUser(999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    #endregion

    #region DeleteUser

    [Fact]
    public void TestDeleteUser()
    {
        var result = _controller.DeleteUser(2);

        Assert.IsType<OkResult>(result);
        _usersService.Received(1).DeleteUser(2);
    }

    [Fact]
    public void TestDeleteUserCannotDeleteItself()
    {
        var result = _controller.DeleteUser(1);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void TestDeleteUserNotFound()
    {
        _usersService.When(x => x.DeleteUser(999)).Do(_ => throw new DataNotFoundException("users", "999"));

        var result = _controller.DeleteUser(999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void TestDeleteUserUnexpectedError()
    {
        _usersService.When(x => x.DeleteUser(4)).Do(_ => throw new Exception("boom"));

        var result = _controller.DeleteUser(4);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region Permissions

    [Fact]
    public void TestGetUserPermissions()
    {
        _permissionsService.GetUserPermissionsById(2).Returns(new List<Permission>
        {
            new Permission { Id = 1, Key = "risks", Name = "Risks", Description = "Risks", Order = 1 },
            new Permission { Id = 2, Key = "assets", Name = "Assets", Description = "Assets", Order = 2 }
        });

        var result = _controller.GetUserPermissions(2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var permissions = Assert.IsType<List<Permission>>(ok.Value);
        Assert.Equal(2, permissions.Count);
    }

    [Fact]
    public void TestGetUserPermissionsNotFound()
    {
        _permissionsService.GetUserPermissionsById(999)
            .Returns(_ => throw new DataNotFoundException("users", "999"));

        var result = _controller.GetUserPermissions(999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestSaveUserPermissions()
    {
        var result = await _controller.SaveUserPermissions(2, new List<int> { 1, 2 });

        Assert.IsType<OkResult>(result.Result);
        await _permissionsService.Received(1).SaveUserPermissionsByIdAsync(2, Arg.Any<List<int>>());
    }

    [Fact]
    public async Task TestSaveUserPermissionsNotFound()
    {
        _permissionsService.SaveUserPermissionsByIdAsync(999, Arg.Any<List<int>>())
            .Returns(_ => throw new DataNotFoundException("users", "999"));

        var result = await _controller.SaveUserPermissions(999, new List<int> { 1 });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestSaveUserPermissionsUnexpectedError()
    {
        _permissionsService.SaveUserPermissionsByIdAsync(5, Arg.Any<List<int>>())
            .Returns(_ => throw new Exception("boom"));

        var result = await _controller.SaveUserPermissions(5, new List<int> { 1 });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public void TestGetAllPermissions()
    {
        _permissionsService.GetAllPermissions().Returns(new List<Permission>
        {
            new Permission { Id = 1, Key = "risks", Name = "Risks", Description = "Risks", Order = 1 }
        });

        var result = _controller.GetAllPermissions();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var permissions = Assert.IsType<List<Permission>>(ok.Value);
        Assert.Single(permissions);
    }

    [Fact]
    public void TestGetAllPermissionsUnexpectedError()
    {
        _permissionsService.GetAllPermissions().Returns(_ => throw new Exception("boom"));

        var result = _controller.GetAllPermissions();

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region SaveUser

    [Fact]
    public void TestSaveUser()
    {
        _usersService.GetUserById(2).Returns(MakeUser(2, "other", false));

        var dto = new UserDto { Id = 2, UserName = "OTHER", Name = "Other", Email = "other@teste.com", Lang = "en" };

        var result = _controller.SaveUser(2, dto);

        Assert.IsType<OkResult>(result);
        _usersService.Received(1).SaveUser(Arg.Any<User>());
    }

    [Fact]
    public void TestSaveUserWithMismatchedIdIsBadRequest()
    {
        var dto = new UserDto { Id = 3, UserName = "other", Name = "Other", Email = "other@teste.com" };

        var result = _controller.SaveUser(2, dto);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, status.StatusCode);
    }

    [Fact]
    public void TestSaveUserReturnsInternalErrorWhenUserIsNull()
    {
        var dto = new UserDto { Id = 60, UserName = "other", Name = "Other", Email = "other@teste.com" };

        var result = _controller.SaveUser(60, dto);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    [Fact]
    public void TestSaveUserNotFound()
    {
        _usersService.GetUserById(999).Returns(_ => throw new DataNotFoundException("users", "999"));

        var dto = new UserDto { Id = 999, UserName = "other", Name = "Other", Email = "other@teste.com" };

        var result = _controller.SaveUser(999, dto);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void TestSaveUserUnexpectedError()
    {
        _usersService.GetUserById(4).Returns(MakeUser(4, "other", false));
        _usersService.When(x => x.SaveUser(Arg.Any<User>())).Do(_ => throw new Exception("boom"));

        var dto = new UserDto { Id = 4, UserName = "other", Name = "Other", Email = "other@teste.com" };

        var result = _controller.SaveUser(4, dto);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    #endregion

    #region CreateUser

    [Fact]
    public void TestCreateUser()
    {
        _usersService.CreateUser(Arg.Any<User>()).Returns(MakeUser(10, "newuser", false));

        var dto = new UserDto
        {
            Id = 0, UserName = "newuser", Name = "New User", Email = "new@teste.com", Lang = "en", Type = "local"
        };

        var result = _controller.CreateUser(dto);

        Assert.NotNull(result.Value);
        Assert.Equal("Test User", result.Value.Name);
        _emailService.Received(1)
            .SendEmailAsync("new@teste.com", "User created", "UserCreated", "en", Arg.Any<object>());
    }

    [Fact]
    public void TestCreateUserWithoutLanguageUsesTheDefaultAndSkipsEmailForSaml()
    {
        _usersService.CreateUser(Arg.Any<User>()).Returns(MakeUser(11, "samluser", false));

        var dto = new UserDto
        {
            Id = 0, UserName = "samluser", Name = "Saml User", Email = "saml@teste.com", Lang = "", Type = "saml"
        };

        var result = _controller.CreateUser(dto);

        Assert.NotNull(result.Value);
        Assert.Equal("en", dto.Lang);
        _emailService.DidNotReceive()
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<object>());
    }

    [Fact]
    public void TestCreateUserWithNonZeroIdIsBadRequest()
    {
        var dto = new UserDto { Id = 7, UserName = "newuser", Name = "New User", Email = "new@teste.com" };

        var result = _controller.CreateUser(dto);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public void TestCreateUserAlreadyExistingUserNameIsBadRequest()
    {
        var dto = new UserDto { Id = 0, UserName = "testUser", Name = "New User", Email = "new@teste.com" };

        var result = _controller.CreateUser(dto);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public void TestCreateUserWithInvalidLanguageIsBadRequest()
    {
        var dto = new UserDto
        {
            Id = 0, UserName = "newuser", Name = "New User", Email = "new@teste.com", Lang = "zz"
        };

        var result = _controller.CreateUser(dto);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, status.StatusCode);
    }

    [Fact]
    public void TestCreateUserAlreadyExistsExceptionIsBadRequest()
    {
        _usersService.CreateUser(Arg.Any<User>())
            .Returns(_ => throw new DataAlreadyExistsException("netrisk", "user", "newuser", "already exists"));

        var dto = new UserDto
        {
            Id = 0, UserName = "newuser", Name = "New User", Email = "new@teste.com", Lang = "en", Type = "local"
        };

        var result = _controller.CreateUser(dto);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void TestCreateUserUnexpectedError()
    {
        _usersService.CreateUser(Arg.Any<User>()).Returns(_ => throw new Exception("boom"));

        var dto = new UserDto
        {
            Id = 0, UserName = "newuser", Name = "New User", Email = "new@teste.com", Lang = "en", Type = "local"
        };

        var result = _controller.CreateUser(dto);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion

    #region GetUserName

    [Fact]
    public void TestGetUserName()
    {
        _usersService.GetUserName(2).Returns("Other User");

        var result = _controller.GetUserName(2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Other User", ok.Value);
    }

    [Fact]
    public void TestGetUserNameNotFound()
    {
        _usersService.GetUserName(999).Returns(_ => throw new DataNotFoundException("users", "999"));

        var result = _controller.GetUserName(999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    #endregion

    #region ChangePassword

    [Fact]
    public void TestChangePasswordWithEmptyRequestIsBadRequest()
    {
        var result = _controller.ChangePassword(1, null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void TestChangePasswordAsAdmin()
    {
        _usersService.GetUserById(2).Returns(MakeUser(2, "other", false));
        _usersService.ChangePassword(2, "newPassword").Returns(true);

        var result = _controller.ChangePassword(2,
            new ChangePasswordRequest { OldPassword = "old", NewPassword = "newPassword" });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Password changed", ok.Value);
    }

    [Fact]
    public void TestChangePasswordFailureReturnsInternalError()
    {
        _usersService.GetUserById(2).Returns(MakeUser(2, "other", false));
        _usersService.ChangePassword(2, "newPassword").Returns(false);

        var result = _controller.ChangePassword(2,
            new ChangePasswordRequest { OldPassword = "old", NewPassword = "newPassword" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    [Fact]
    public void TestChangePasswordExceptionIsNotFound()
    {
        _usersService.GetUserById(2).Returns(MakeUser(2, "other", false));
        _usersService.ChangePassword(2, "newPassword").Returns(_ => throw new Exception("boom"));

        var result = _controller.ChangePassword(2,
            new ChangePasswordRequest { OldPassword = "old", NewPassword = "newPassword" });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public void TestChangePasswordOfAnotherUserAsNonAdminIsUnauthorized()
    {
        var result = _nonAdminController.ChangePassword(2,
            new ChangePasswordRequest { OldPassword = "old", NewPassword = "newPassword" });

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public void TestChangePasswordAsNonAdminWithWrongOldPasswordIsUnauthorized()
    {
        _nonAdminUsersService.VerifyPassword(1, "wrong").Returns(false);

        var result = _nonAdminController.ChangePassword(1,
            new ChangePasswordRequest { OldPassword = "wrong", NewPassword = "newPassword" });

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public void TestChangePasswordAsNonAdminOwnPassword()
    {
        _nonAdminUsersService.VerifyPassword(1, "old").Returns(true);
        _nonAdminUsersService.GetUserById(1).Returns(MakeUser(1, "testUser", false));
        _nonAdminUsersService.ChangePassword(1, "newPassword").Returns(true);

        var result = _nonAdminController.ChangePassword(1,
            new ChangePasswordRequest { OldPassword = "old", NewPassword = "newPassword" });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Password changed", ok.Value);
    }

    #endregion

    #region Listings

    [Fact]
    public async Task TestListUsersAsync()
    {
        _usersService.ListActiveUsersAsync().Returns(new List<UserListing>
        {
            new UserListing { Id = 1, Name = "Test User", Username = "testUser" },
            new UserListing { Id = 2, Name = "Other User", Username = "other" }
        });

        var result = await _controller.ListUsersAsync();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var users = Assert.IsType<List<UserListing>>(ok.Value);
        Assert.Equal(2, users.Count);
    }

    [Fact]
    public async Task TestListUsersAsyncUnexpectedError()
    {
        _usersService.ListActiveUsersAsync().Returns<Task<List<UserListing>>>(_ => throw new Exception("boom"));

        var result = await _controller.ListUsersAsync();

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
    }

    #endregion
}
