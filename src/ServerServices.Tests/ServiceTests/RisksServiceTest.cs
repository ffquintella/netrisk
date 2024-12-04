using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(RisksService))]
public class RisksServiceTest: BaseServiceTest
{
    private readonly IRisksService _risksService;
    
    public RisksServiceTest()
    {
        _risksService = _serviceProvider.GetRequiredService<IRisksService>();
    }

    [Fact]
    public async Task TestGetVulnerabilitiesAsync()
    {
        var vulnerabilities = await _risksService.GetVulnerabilitiesAsync(1);
        
        Assert.NotNull(vulnerabilities);
        
        Assert.Equal(2, vulnerabilities.Count);
    }

    [Fact]
    public async Task TestGetIncidentResponsePlanAsync()
    {
        var irp = await _risksService.GetIncidentResponsePlanAsync(1);
        Assert.NotNull(irp);
        Assert.Equal(1, irp.Id);
    }
}