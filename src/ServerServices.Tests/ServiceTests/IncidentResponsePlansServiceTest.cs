﻿using System.Threading.Tasks;
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
        Assert.Equal(1, result1.CreatedById);
        Assert.Equal((int)IntStatus.AwaitingApproval, result1.Status);
        

    }

    [Fact]
    public async Task TestUpdateAsync()
    {
        
        var oldIrps = await _incidentResponsePlansService.GetAllAsync();
        
        Assert.NotNull(oldIrps);
        Assert.True(oldIrps.Count > 0);
        
        var newirp = oldIrps[0];
        
        newirp.Name = "IRP16";
        newirp.Description = "D16";
        
        var user = new User
        {
            Value = 1,
            Username = System.Text.Encoding.UTF8.GetBytes("testuser"),
            Email = System.Text.Encoding.UTF8.GetBytes("no@mail.com"),
            Admin = false
        };
        
        var result1 = await _incidentResponsePlansService.UpdateAsync(newirp, user);
        
        Assert.NotNull(result1);
        Assert.Equal(newirp.Id, result1.Id);
        Assert.Equal("IRP16", result1.Name);
        Assert.Equal("D16", result1.Description);
        
        var result2 = await _incidentResponsePlansService.GetByIdAsync(newirp.Id);
        
        Assert.NotNull(result2);
        Assert.Equal(newirp.Id, result2.Id);
        
    }

    [Fact]
    public async Task TestGetByIdAsync()
    {
        var irp = await _incidentResponsePlansService.GetByIdAsync(1);
        
        Assert.NotNull(irp);
        Assert.Equal(1, irp.Id);
        
    }

}