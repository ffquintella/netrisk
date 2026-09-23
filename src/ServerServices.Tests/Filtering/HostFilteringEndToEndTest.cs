using System;
using System.Globalization;
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
    /// Runs <paramref name="body"/> with the UI culture forced, since the mapper's external names
    /// are resolved per request against <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    private static async Task InCulture(string culture, Func<Task> body)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            await body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>
    /// Regression: the desktop client hard-codes <c>hostName@=</c>, and the mapper used to know
    /// only the localized name, so on a pt-BR machine the host search box raised
    /// GridifyMapperException -> HTTP 409 and silently returned nothing. Every column now answers
    /// to its invariant name in every culture. See CHANGELOG.md, [NEXT].
    /// </summary>
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public async Task Invariant_column_name_filters_in_any_culture(string culture) =>
        await InCulture(culture, async () =>
        {
            var ids = (await Ids("hostname@=web")).OrderBy(i => i).ToArray();
            Assert.Equal(new[] { 1, 2 }, ids);
        });

    /// <summary>The literal the GUI actually sends — same name, the casing of a C# property.</summary>
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public async Task The_clients_hard_coded_host_name_filter_works_in_any_culture(string culture) =>
        await InCulture(culture, async () =>
        {
            var ids = (await Ids("hostName@=web")).OrderBy(i => i).ToArray();
            Assert.Equal(new[] { 1, 2 }, ids);
        });

    /// <summary>
    /// The invariant aliases are additive: a human typing the translated name still gets rows.
    /// The expected name is read from the localizer under the same culture, so the test asserts
    /// the wiring rather than a particular translation.
    /// </summary>
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public async Task Localized_column_name_still_filters(string culture) =>
        await InCulture(culture, async () =>
        {
            var ids = (await Ids($"{Name("hostname")}@=web")).OrderBy(i => i).ToArray();
            Assert.Equal(new[] { 1, 2 }, ids);
        });

    /// <summary>
    /// Sorting reads the same map, so the invariant name has to be accepted there too — the
    /// export endpoint and the GUI's column headers both send bare property names.
    /// </summary>
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public async Task Invariant_column_name_sorts_in_any_culture(string culture) =>
        await InCulture(culture, async () =>
        {
            var ids = await Ids(sorts: "-hostname");
            Assert.Equal(new[] { 2, 1, 3 }, ids);
        });

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
