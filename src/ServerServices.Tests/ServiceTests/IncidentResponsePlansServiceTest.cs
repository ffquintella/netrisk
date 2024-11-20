using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(IncidentResponsePlansService))]
public class IncidentResponsePlansServiceTest: BaseServiceTest
{
    private readonly IIncidentResponsePlansService _incidentResponsePlansService;
    
    public IncidentResponsePlansServiceTest()
    {
        _incidentResponsePlansService = _serviceProvider.GetRequiredService<IIncidentResponsePlansService>();
    }

    [Fact]
    public async Task TestGetAllAsync()
    {

        var result1 = await _incidentResponsePlansService.GetAllAsync();
        
        Assert.NotNull(result1);
        Assert.Equal(15, result1.Count);


    }
    
    [Fact]
    public async Task TestCreateAsync()
    {

        var newirp = new IncidentResponsePlan
        {
            Name = "IRP16",
            Description = "D16"
        };

        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("no@mail.com"),
            Admin = false
        };
        
        var result1 = await _incidentResponsePlansService.CreateAsync(newirp, user);
        
        Assert.NotNull(result1);
        Assert.Equal(0, result1.Id);
        Assert.Equal("IRP16", result1.Name);
        Assert.Equal("D16", result1.Description);
        Assert.Equal(1, result1.CreatedBy.Value);
        Assert.Equal("testuser", System.Text.Encoding.UTF8.GetString(result1.CreatedBy.Username));
        Assert.Equal((int)IntStatus.AwaitingApproval, result1.Status);
        

    }

}