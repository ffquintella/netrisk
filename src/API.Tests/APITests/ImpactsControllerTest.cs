using System.Collections.Generic;
using API.Controllers;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Globalization;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(ImpactsController))]
public class ImpactsControllerTest : BaseControllerTest
{
    private readonly IImpactsService _impactsService = Substitute.For<IImpactsService>();
    private readonly ImpactsController _controller;

    public ImpactsControllerTest()
    {
        _impactsService.GetAll().Returns(new List<LocalizableListItem>
        {
            new() { Key = 1, Value = "Low", LocalizedValue = "Low" },
            new() { Key = 2, Value = "Medium", LocalizedValue = "Medium" },
            new() { Key = 3, Value = "High", LocalizedValue = "High" }
        });

        _controller = ResolveController<ImpactsController>(s => s.AddSingleton(_impactsService));
    }

    [Fact]
    public void TestGetAll()
    {
        var result = _controller.GetAll();

        Assert.Equal(3, result.Count);
        Assert.Equal("Low", result[0].Value);
        Assert.Equal(3, result[2].Key);
        _impactsService.Received(1).GetAll();
    }
}
