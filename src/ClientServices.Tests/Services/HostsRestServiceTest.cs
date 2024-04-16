using ClientServices.Interfaces;
using ClientServices.Services;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(HostsRestService))]
public class HostsRestServiceTest: BaseServiceTest
{

    private readonly IHostsService _hostsService;
    
    public HostsRestServiceTest()
    {
        _hostsService = _serviceProvider.GetRequiredService<IHostsService>();
    }
    
    
    [Fact]
    public async void TestGetAllHostServiceAsync()
    {
        // Arrange
        
        // Act
        // Call the method you're testing.
        
        var hosts = await _hostsService.GetAllHostServiceAsync(1);
        
        // Assert
        // Verify the results.
        
        Assert.NotNull(hosts);


    }
    

}