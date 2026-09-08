using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Tests.Mock;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using RestSharp;
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
        // Resolved against StubRestBackend rather than the class-wide NSubstitute container: this is
        // a PATCH, so it now goes through RestServiceBase.MutatingClient, and MockSetup can only
        // stand in for GetReliableClient — IRestService.GetClient returns a concrete RestClient, so
        // its stub hands back null.
        using var backend = new StubRestBackend();
        var service = ResolveWith<IRisksService>(backend);

        backend.OnStatus(Method.Patch, "/Risks/1/IncidentResponsePlan/1", HttpStatusCode.OK);
        backend.OnStatus(Method.Patch, "/Risks/2/IncidentResponsePlan/2", HttpStatusCode.NotFound);

        await service.AssociateRiskToIncidentResponsePlanAsync(1,1);

        await Assert.ThrowsAsync<DataNotFoundException>(async () => await service.AssociateRiskToIncidentResponsePlanAsync(2,2));
    }
}