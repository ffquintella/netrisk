using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(RisksController))]
public class RisksControllerTest: BaseControllerTest
{
    private readonly RisksController _risksController;

    public RisksControllerTest()
    {
        _risksController = _serviceProvider.GetRequiredService<RisksController>();
    }
    
    // Add tests here


    [Fact]
    public async Task TestGetOpenVulnerabilities()
    {
        // Arrange
        var expected = 1;
        
        // Act
        var result = await _risksController.GetOpenVulnerabilities(1);
        
        // Assert
        Assert.NotNull(result);
        
        Assert.IsType<OkObjectResult>(result.Result);

        var okResult = (OkObjectResult)result.Result;
        
        var resultList = (List<Vulnerability>)okResult.Value;

        Assert.NotNull(resultList);
        Assert.Equal(2, resultList.Count);
    }

    [Fact]
    public async Task TestGetIncidentResponsePlan()
    {
        
        var result = await  _risksController.GetIncidentResponsePlan(1);

        Assert.NotNull(result);
        
        Assert.IsType<OkObjectResult>(result.Result);
        
        var okResult = (OkObjectResult)result.Result;
        var resultObj = (IncidentResponsePlan)okResult.Value;
        
        Assert.NotNull(resultObj);
        Assert.Equal(1, resultObj.Id);
    }

}