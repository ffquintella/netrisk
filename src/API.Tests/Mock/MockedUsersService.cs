using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public class MockedUsersService
{
    public static IUsersService Create()
    {
        var usersService = Substitute.For<IUsersService>();
        return usersService;
    }
}