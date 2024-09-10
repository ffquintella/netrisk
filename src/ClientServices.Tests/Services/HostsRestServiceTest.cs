using System.Threading.Tasks;
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
    public async Task TestGetAllHostServiceAsync()
    {
        // Arrange
        
        // Act
        // Call the method you're testing.
        
        var hosts = await _hostsService.GetAllHostServiceAsync(1);
        
        // Assert
        // Verify the results.
        
        Assert.NotNull(hosts);


    }


    [Fact]
    public async Task TestGetAllHostVulnerabilitiesAsync()
    {
       // /Hosts/{hostId}/Vulnerabilities
       // Act
       // Call the method you're testing.
        
       var vuls = await _hostsService.GetAllHostVulnerabilitiesAsync(1);
        
       // Assert
       // Verify the results.
        
       Assert.NotNull(vuls);
       Assert.Equal(2, vuls.Count);
    }
}