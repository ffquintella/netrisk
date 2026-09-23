using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using ServerServices.Filtering;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Filtering;

/// <summary>
/// Filtering end to end: a Sieve expression through the translator, the localized mapper and the
/// real service, against seeded rows. The translator's own tests use a toy type — these prove the
/// same strings still select the right rows through the production graph. See CHANGELOG.md, [NEXT].
/// </summary>
public class HostFilteringEndToEndTest : InMemoryServiceTestBase
{
    private readonly IHostsService _svc;
    private readonly Microsoft.Extensions.Localization.IStringLocalizer _localizer;

    public HostFilteringEndToEndTest()
    {
        _svc = GetService<IHostsService>();
        _localizer = GetService<ILocalizationService>().GetLocalizer();

        Seed(ctx =>
        {
            ctx.Hosts.Add(Host(1, "web-a", "10.0.0.1", "linux", (short)1));
            ctx.Hosts.Add(Host(2, "web-b", "10.0.0.2", "linux", (short)2));
            ctx.Hosts.Add(Host(3, "db-c", "10.0.0.3", "windows", (short)1));
        });
    }

    private static Host Host(int id, string name, string ip, string os, short status) => new()
    {
        Id = id, HostName = name, Ip = ip, Os = os, Status = status,
        Source = "manual", RegistrationDate = new DateTime(2026, 1, 1).AddDays(id)
    };

    /// <summary>
    /// The external name of a filterable column. Names are localized, so a test that hard-coded
    /// the English spelling would pass or fail on the machine's culture rather than on the code.
    /// </summary>
    private string Name(string key) => _localizer[key];

    private async Task<int[]> Ids(string? filters = null, string? sorts = null, int? page = null, int? pageSize = null)
    {
        var (rows, _) = await _svc.GetFiltredAsync(
            new ListQuery { Filters = filters, Sorts = sorts, Page = page, PageSize = pageSize });
        return rows.Select(h => h.Id).ToArray();
    }

    [Fact]
    public async Task No_filter_returns_everything()
    {
        var ids = (await Ids()).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, ids);
    }

    [Fact]
    public async Task Equals_filter_selects_matching_rows()
    {
        var ids = (await Ids("os==linux")).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { 1, 2 }, ids);
    }

    /// <summary>The idiom the REST client hard-codes for bulk label lookups.</summary>
    [Fact]
    public async Task Value_list_selects_any_of()
    {
        var ids = (await Ids("id==1|3")).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { 1, 3 }, ids);
    }

    [Fact]
    public async Task Contains_filter_matches_a_substring()
    {
        var ids = (await Ids($"{Name("hostname")}@=web")).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { 1, 2 }, ids);
    }

    [Fact]
    public async Task Two_filters_are_anded()
    {
        var ids = await Ids($"os==linux,{Name("status")}==1");
        Assert.Equal(new[] { 1 }, ids);
    }

    /// <summary>
    /// The parenthesisation the translator adds, proved through the real stack: without it this
    /// reads as (id=1) OR (id=2 AND os=windows) and returns row 1 alone.
    /// </summary>
    [Fact]
    public async Task Value_list_stays_atomic_beside_another_filter()
    {
        var ids = (await Ids("id==1|2,os==linux")).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { 1, 2 }, ids);
    }

    [Fact]
    public async Task Descending_sort_is_honoured()
    {
        var ids = await Ids(sorts: "-id");
        Assert.Equal(new[] { 3, 2, 1 }, ids);
    }

    [Fact]
    public async Task Paging_returns_the_requested_slice_and_the_full_total()
    {
        var (rows, total) = await _svc.GetFiltredAsync(
            new ListQuery { Sorts = "id", Page = 2, PageSize = 1 });

        Assert.Equal(3, total);
        Assert.Equal(new[] { 2 }, rows.Select(h => h.Id).ToArray());
    }

    [Fact]
    public async Task Page_size_is_clamped_to_the_maximum()
    {
        var (rows, _) = await _svc.GetFiltredAsync(new ListQuery { PageSize = 999_999 });

        Assert.True(rows.Count <= FilterBounds.MaxPageSize);
    }

    /// <summary>
    /// The whitelist is the reason the mapper exists. A property that is not mapped must be
    /// rejected rather than silently ignored, or the filter surface is every public property.
    /// </summary>
    [Fact]
    public async Task Unmapped_property_is_rejected()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => _svc.GetFiltredAsync(
            new ListQuery { Filters = "source==manual" }));
    }
}
