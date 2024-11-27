using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
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
        
        var result = await _incidentResponsePlanController.GetAllAsync();
        
        Assert.NotNull(result);
        
        Assert.IsType<OkObjectResult>(result.Result);

        var okResult = (OkObjectResult)result.Result;
        
        var resultList = (List<IncidentResponsePlan>)okResult.Value;

        Assert.NotNull(resultList);
        Assert.Equal(2, resultList.Count);

    }
}