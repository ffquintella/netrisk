using System.Threading.Tasks;
using ClientServices.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
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

    [Fact]
    public async Task TestAssociateRiskToIncidentResponsePlanAsync()
    {
        await _risksService.AssociateRiskToIncidentResponsePlanAsync(1,1);

        await Assert.ThrowsAsync<DataNotFoundException>(async () => await _risksService.AssociateRiskToIncidentResponsePlanAsync(2,2));
    }
}