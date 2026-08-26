using DAL.Entities;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// The same fixture user as <see cref="MockedUsersService"/>, minus the administrator flag.
///
/// The factory is called <c>Build</c> rather than <c>Create</c> on purpose: the DI convention
/// auto-registers every static <c>Create()</c> in this namespace, and a second <see cref="IUsersService"/>
/// registration would replace the shared admin fixture for every test in the suite. A test asks for
/// this one explicitly through <c>ResolveController</c>. It exists because several Track 8 controls
/// treat an administrator differently on purpose — an admin may open any entity's review campaign so
/// that somebody can unblock one whose reviewer has left — and asserting the *refusal* needs a caller
/// who is not one.
/// </summary>
public static class MockedNonAdminUsersService
{
    public static IUsersService Build()
    {
        var usersService = Substitute.For<IUsersService>();

        var user = new User
        {
            Admin = false,
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
