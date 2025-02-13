using DAL.Entities;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedUsersService
{
    public static IUsersService Create()
    {
        var usersService = Substitute.For<IUsersService>();

        usersService.GetUserAsync("testUser").Returns(new User()
        {
            Admin = true,
            Lang = "en",
            Name = "testUser",
            Password = System.Text.Encoding.UTF8.GetBytes("testUser"),
            Value = 1,
            Email = System.Text.Encoding.UTF8.GetBytes("testUser@teste.com")
        });
        
        return usersService;
    }
}