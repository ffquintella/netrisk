using System.Linq;
using DAL.Entities;
using Gridify;
using JetBrains.Annotations;
using ServerServices.Filtering;
using Xunit;

namespace ServerServices.Tests.Filtering;

/// <summary>
/// The job host's filter provider (T237, S39). The job host needs one only because <c>RisksService</c> takes
/// it; no job takes a filter from a caller, so every entity's whitelist is empty — and an empty whitelist has
/// to refuse a filter, never fall back to reflecting over the entity's columns.
/// </summary>
[TestSubject(typeof(EmptyEntityFilterMapperProvider))]
public class EmptyEntityFilterMapperProviderTest
{
    [Fact]
    public void EveryEntityGetsAnEmptyWhitelist()
    {
        var provider = new EmptyEntityFilterMapperProvider();

        Assert.Empty(provider.For<Vulnerability>().GetCurrentMaps());
        Assert.Empty(provider.For<Risk>().GetCurrentMaps());
        Assert.Empty(provider.For<Host>().GetCurrentMaps());
    }

    [Fact]
    public void AFilterNamingAnyPropertyIsRefused()
    {
        var mapper = new EmptyEntityFilterMapperProvider().For<Vulnerability>();
        var query = new[] { new Vulnerability { Id = 1, Title = "x" } }.AsQueryable();

        Assert.Throws<GridifyMapperException>(() => query.ApplyFiltering("title=x", mapper).ToList());
    }

    [Fact]
    public void AnEmptyFilterStillLists()
    {
        var mapper = new EmptyEntityFilterMapperProvider().For<Vulnerability>();
        var query = new[] { new Vulnerability { Id = 1, Title = "x" } }.AsQueryable();

        Assert.Single(query.ApplyFiltering(string.Empty, mapper));
    }
}
