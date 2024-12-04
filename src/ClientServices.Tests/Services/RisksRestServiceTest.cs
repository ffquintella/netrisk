using System.Threading.Tasks;
using ClientServices.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClientServices.Tests.Services;

public class RisksRestServiceTest: BaseServiceTest
{
    private readonly IRisksService _risksService;
    
    public RisksRestServiceTest()
    {
        _risksService = _serviceProvider.GetRequiredService<IRisksService>();
    }
    
    [Fact]
    public async Task TestGetIncidentResponsePlanAsync()
    {

        var irps = await _risksService.GetIncidentResponsePlanAsync(1);
        
        Assert.NotNull(irps);
        Assert.Equal("Test", irps.Name);

        
    }
}