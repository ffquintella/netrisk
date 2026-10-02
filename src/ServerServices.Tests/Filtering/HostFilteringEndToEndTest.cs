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
            var a = Host(1, "web-a", "10.0.0.1", "linux", (short)1);
            a.Criticality = 5; a.Environment = "Produção"; a.Owner = "Ana"; a.Source = "nessus";
            a.RiskScore = 80; a.LastVerificationDate = new DateTime(2026, 9, 1);

            var b = Host(2, "web-b", "10.0.0.2", "linux", (short)2);
            b.Criticality = 3; b.Environment = "Homolog"; b.Owner = "Bruno"; b.Source = "jira-assets";
            b.RiskScore = 40; b.LastVerificationDate = new DateTime(2026, 6, 1);

            // Host 3 carries none of the facet columns: the "Not set" case.
            var c = Host(3, "db-c", "10.0.0.3", "windows", (short)1);

            ctx.Hosts.Add(a);
            ctx.Hosts.Add(b);
            ctx.Hosts.Add(c);
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
    [Theory]
    [InlineData("comment==x")]
    [InlineData("properties@=x")]
    [InlineData("macAddress==00:11")]
    [InlineData("externalId==1042")]
    public async Task Unmapped_property_is_rejected(string filter)
    {
        // `source` was the example here until S38 put it on the whitelist; these stay off it.
        await Assert.ThrowsAnyAsync<Exception>(() => _svc.GetFiltredAsync(
            new ListQuery { Filters = filter }));
    }

    // --- S38 §5.1: the Hosts view facets --------------------------------------------------------

    [Theory]
    [InlineData("criticality==5", new[] { 1 })]
    [InlineData("criticality>=3", new[] { 1, 2 })]
    [InlineData("environment==Produção", new[] { 1 })]
    [InlineData("environment==Homolog", new[] { 2 })]
    [InlineData("owner==Bruno", new[] { 2 })]
    [InlineData("owner@=An", new[] { 1 })]
    [InlineData("source==nessus", new[] { 1 })]
    [InlineData("source==manual", new[] { 3 })]
    [InlineData("riskScore>=50", new[] { 1 })]
    [InlineData("riskScore<50", new[] { 2 })]
    [InlineData("lastVerificationDate>=2026-08-01", new[] { 1 })]
    [InlineData("lastVerificationDate<2026-08-01", new[] { 2 })]
    public async Task A_facet_column_filters(string filter, int[] expected)
    {
        var ids = (await Ids(filter)).OrderBy(i => i).ToArray();
        Assert.Equal(expected, ids);
    }

    /// <summary>The criticality facet's "Not set" option.</summary>
    [Fact]
    public async Task Criticality_null_selects_hosts_without_one()
    {
        var ids = (await Ids("criticality==null")).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { 3 }, ids);
    }

    /// <summary>The composed string the Hosts view sends: every facet ANDed with the search box.</summary>
    [Fact]
    public async Task Facets_compose_with_the_search_box()
    {
        var ids = await Ids("hostName@=web,status==1,teamId==2,criticality==5,environment==Produção");
        Assert.Empty(ids);

        ids = await Ids("hostName@=web,status==1,criticality==5,environment==Produção");
        Assert.Equal(new[] { 1 }, ids);
    }

    /// <summary>
    /// An environment containing the filter grammar's own separators (<c>,</c> and <c>|</c>) must
    /// select exactly its host. The literal below is what <c>GUIClient.Tools.Hosts.HostsFilterComposer</c>
    /// emits for the facet value <c>Prod, EU|A</c>; it is hard-coded because ServerServices.Tests
    /// does not reference GUIClient (the composer's own test pins the same string on that side).
    /// </summary>
    [Fact]
    public async Task Environment_with_filter_separators_selects_only_its_host()
    {
        Seed(ctx =>
        {
            var special = Host(4, "app-d", "10.0.0.4", "linux", (short)1);
            special.Environment = "Prod, EU|A";

            var plain = Host(5, "app-e", "10.0.0.5", "linux", (short)1);
            plain.Environment = "Prod";

            var sibling = Host(6, "app-f", "10.0.0.6", "linux", (short)1);
            sibling.Environment = "Prod, EU|B";

            ctx.Hosts.Add(special);
            ctx.Hosts.Add(plain);
            ctx.Hosts.Add(sibling);
        });

        var ids = await Ids(@"environment==Prod\, EU\|A");

        Assert.Equal(new[] { 4 }, ids);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public async Task Facet_columns_filter_by_their_invariant_name_in_any_culture(string culture) =>
        await InCulture(culture, async () =>
        {
            var ids = (await Ids("criticality==5,environment==Produção,owner==Ana,source==nessus"))
                .ToArray();
            Assert.Equal(new[] { 1 }, ids);
        });

    [Fact]
    public async Task Facet_columns_sort()
    {
        Assert.Equal(new[] { 1, 2 }, (await Ids("riskScore>=0", sorts: "-riskScore")));
        Assert.Equal(new[] { 2, 1 }, (await Ids("criticality>=1", sorts: "criticality")));
    }
}
