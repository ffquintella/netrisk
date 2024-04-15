using System;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.DI;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(ClientRegistrationService))]
public class ClientRegistrationServiceTest: BaseServiceTest
{
    
    private readonly IClientRegistrationService _clientRegistrationService;
    
    public ClientRegistrationServiceTest()
    {
        _clientRegistrationService = _serviceProvider.GetRequiredService<IClientRegistrationService>();
    }
    
    [Fact]
    public async void TestFindApprovedRegistrationAsync()
    {
        // Arrange


        // Act
        // Call the method you're testing.
        
        var regId1 = await _clientRegistrationService.FindApprovedRegistrationAsync("id1");
        var regId2 = await _clientRegistrationService.FindApprovedRegistrationAsync("id2");

        // Assert
        // Verify the results.
        
        Assert.Null(regId1);
        Assert.NotNull(regId2);
        

    }
}