using DAL.Entities;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedUsersService
{
    public static IUsersService Create()
    {
        var usersService = Substitute.For<IUsersService>();

        var user = new User()
        {
            Admin = true,
            Lang = "en",
            Name = "testUser",
            Password = "testUser"u8.ToArray(),
            Value = 1,
            Email = "testUser@teste.com"
        };
        usersService.GetUserAsync("testUser").Returns(user);
        usersService.GetUser("testUser").Returns(user);
        
        return usersService;
    }
}
