using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Interfaces;
using ServerServices.Services;
using Sieve.Models;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(HostsService))]
public class HostsServiceTest: BaseServiceTest
{

    private readonly IHostsService _hostsService;
    
    public HostsServiceTest()
    {
        _hostsService = _serviceProvider.GetRequiredService<IHostsService>();
    }
    
    [Fact]
    public async void TestGetFiltredAsync()
    {
        // Arrange


        // Act
        // Call the method you're testing.
        
        var result1 = await _hostsService.GetFiltredAsync(new SieveModel()
        {
            Page = 1,
            PageSize = 10,
        });

        var result2 = await _hostsService.GetFiltredAsync(new SieveModel()
        {
            Page = 1,
            PageSize = 10,
            Filters = "os==linux"
        });

        // Assert
        // Verify the results.
        
        Assert.NotNull(result1);
        Assert.Equal(15, result1.Item2);
        Assert.Equal(10, result1.Item1.Count);
        
        Assert.NotNull(result2);
        Assert.Equal(4, result2.Item2);
        Assert.Equal(4, result2.Item1.Count);

    }
}