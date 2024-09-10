using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(UsersService))]
public class UsersServiceTest: BaseServiceTest
{
    private readonly IUsersService _usersService;
    
    public UsersServiceTest()
    {
        _usersService = _serviceProvider.GetRequiredService<IUsersService>();
    }
    
    
    [Fact]
    public async Task TestFindEnabledActiveUserAsync()
    {
        // Arrange
        
        // Act
        // Call the method you're testing.
        
        var user = await _usersService.FindEnabledActiveUserByNameAsync("u1");
        var user2 = await _usersService.FindEnabledActiveUserByNameAsync("u2");

        // Assert
        // Verify the results.
        
        Assert.NotNull(user);
        Assert.Null(user2);

    }
    
    [Fact]
    public async Task TestGetByIdAsync()
    {
        // Arrange
        
        // Act
        // Call the method you're testing.
        
        var user = await _usersService.GetUserByIdAsync(1);

        // Assert
        // Verify the results.
        
        Assert.NotNull(user);


    }
}