using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClientServices.Tests.Services;

public class IncidentResponsePlansRestServiceTests: BaseServiceTest
{
    private readonly IIncidentResponsePlansService _incidentResponsePlansService;
    
    public IncidentResponsePlansRestServiceTests()
    {
        _incidentResponsePlansService = _serviceProvider.GetRequiredService<IIncidentResponsePlansService>();
    }
    
    [Fact]
    public async Task TestGetAllAsync()
    {
        
        var plans = await _incidentResponsePlansService.GetAllAsync();
        
        Assert.NotNull(plans);
        
        Assert.Equal(2, plans.Count);
        
    }

    [Fact]
    public async Task TestCreateAsync()
    {
        
        var plan = new IncidentResponsePlan()
        {
            Id = 0,
            Name = "TestCreate",
            Description = "Test"
        };
        
        var createdPlan = await _incidentResponsePlansService.CreateAsync(plan);
        
        Assert.NotNull(createdPlan);
        Assert.NotEqual(0, createdPlan.Id);
        Assert.Equal("TestCreate", createdPlan.Name);
        
    }

    [Fact]
    public async Task TestUpdateAsync()
    {
        var plan = new IncidentResponsePlan()
        {
            Id = 1,
            Name = "TestUpdate",
            Description = "Test"
        };
        
        var updatedPlan = await _incidentResponsePlansService.UpdateAsync(plan);
        
        Assert.NotNull(updatedPlan);
        Assert.Equal(1, updatedPlan.Id);
        Assert.Equal("TestUpdate", updatedPlan.Name);
    }
}