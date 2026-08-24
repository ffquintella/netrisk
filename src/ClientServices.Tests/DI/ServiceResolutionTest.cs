using System;
using System.Collections.Generic;
using System.Linq;
using ClientServices.Interfaces;
using ClientServices.Tests.Mock;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClientServices.Tests.DI;

/// <summary>
/// The container registers services by convention, so this is what keeps that convention honest:
/// if a new service arrives with a dependency nothing supplies, it fails here by name rather than
/// inside whichever test happens to resolve it first.
/// </summary>
public class ServiceResolutionTest
{
    public static TheoryData<Type> Contracts()
    {
        var data = new TheoryData<Type>();
        foreach (var contract in ServiceRegistration.DiscoverableContracts().OrderBy(t => t.Name))
            data.Add(contract);
        return data;
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void TestEveryDiscoveredServiceResolves(Type contract)
    {
        var provider = ServiceRegistration.GetServiceProvider(
            s => s.AddSingleton<IRestService>(new StubRestBackend()));

        var resolved = provider.GetService(contract);

        Assert.NotNull(resolved);
        Assert.IsAssignableFrom(contract, resolved);
    }

    [Fact]
    public void TestTheConventionFindsTheRestServices()
    {
        var names = ServiceRegistration.DiscoverableContracts().Select(t => t.Name).ToList();

        Assert.Contains(nameof(IRisksService), names);
        Assert.Contains(nameof(IHostsService), names);
        Assert.Contains(nameof(IAssessmentsService), names);
        Assert.True(names.Count > 30, $"expected the scan to find most services, found {names.Count}");
    }
}
