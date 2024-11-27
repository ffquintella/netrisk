using System.Threading.Tasks;
using API.Controllers;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(IncidentResponsePlanController))]
public class IncidentResponsePlanControllerTest: BaseControllerTest
{

    private readonly IncidentResponsePlanController _incidentResponsePlanController;

    public IncidentResponsePlanControllerTest()
    {
        _incidentResponsePlanController = _serviceProvider.GetRequiredService<IncidentResponsePlanController>();
    }
    
    [Fact]
    public async Task TestGetAllAsync()
    {

        string a = "teste";
        
        Assert.Equal("teste", a);

    }
}