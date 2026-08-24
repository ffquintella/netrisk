using System.Collections.Generic;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(TechnologiesController))]
public class TechnologiesControllerTest : BaseControllerTest
{
    private readonly ITechnologiesService _technologiesService = Substitute.For<ITechnologiesService>();
    private readonly TechnologiesController _controller;

    public TechnologiesControllerTest()
    {
        _technologiesService.GetAll().Returns(new List<Technology>
        {
            new() { Value = 1, Name = "Linux" },
            new() { Value = 2, Name = "Windows" },
            new() { Value = 3, Name = "MacOS" }
        });

        _controller = ResolveController<TechnologiesController>(s => s.AddSingleton(_technologiesService));
    }

    [Fact]
    public void TestGetAll()
    {
        var result = _controller.GetAll();

        Assert.Equal(3, result.Count);
        Assert.Equal("Linux", result[0].Name);
        Assert.Equal(3, result[2].Value);
        _technologiesService.Received(1).GetAll();
    }
}
