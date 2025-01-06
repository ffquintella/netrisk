using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(IncidentsService))]
public class IncidentsServiceTest: BaseServiceTest
{
    private readonly IIncidentsService _incidentsService;
    
    public IncidentsServiceTest()
    {
        _incidentsService = _serviceProvider.GetRequiredService<IIncidentsService>();
    }
    
    [Fact]
    public async Task TestGetAllAsync()
    {

        var result1 = await _incidentsService.GetAllAsync();
        
        Assert.NotNull(result1);
        Assert.Equal(2, result1.Count);


    }
    
    [Fact]
    public async Task TestCreateAsync()
    {

        var newinc = new Incident()
        {
            Name = "IRP16",
            Description = "D16",
            Status = 0,
            Id = 6
        };

        var user = new User
        {
            Value = 1,
            //Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Login = "testuser",
            Email = System.Text.Encoding.UTF8.GetBytes("no@mail.com"),
            Admin = false
        };
        
        var result1 = await _incidentsService.CreateAsync(newinc, user);
        
        Assert.NotNull(result1);
        Assert.Equal(0, result1.Id);
        Assert.Equal("IRP16", result1.Name);
        Assert.Equal("D16", result1.Description);
        Assert.NotEqual(0, result1.Status);
        

    }
    
    [Fact]
    public async Task TestUpdateAsync()
    {

        var updinc = new Incident()
        {
            Name = "IRP16",
            Description = "D16",
            Status = 8,
            Id = 6
        };

        var user = new User
        {
            Value = 1,
            //Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Login = "testuser",
            Email = System.Text.Encoding.UTF8.GetBytes("no@mail.com"),
            Admin = false
        };

        var oldInc = await _incidentsService.GetByIdAsync(1);
        Assert.NotNull(oldInc);
        
        await Assert.ThrowsAsync<DataNotFoundException>(async () => await _incidentsService.UpdateAsync(updinc, user));
        
        oldInc.Name = "IRP20";
        
        await _incidentsService.UpdateAsync(oldInc, user);
        
        var newInc = await _incidentsService.GetByIdAsync(1);
        
        Assert.NotNull(newInc);
        Assert.Equal(1, newInc.Id);
        Assert.Equal("IRP20", newInc.Name);
        

    }

    [Fact]
    public async Task TestDeleteAsync()
    {
        
        await Assert.ThrowsAsync<DataNotFoundException>(async () => await _incidentsService.GetByIdAsync(100));
        
        var oldInc = await _incidentsService.GetByIdAsync(1);
        Assert.NotNull(oldInc);
        
        await _incidentsService.DeleteByIdAsync(1);
        
        await Assert.ThrowsAsync<DataNotFoundException>(async () => await _incidentsService.GetByIdAsync(1));
    }
}