using System.Linq;
using Gridify;
using ServerServices.Filtering;
using Xunit;

namespace ServerServices.Tests.Filtering;

/// <summary>
/// The translator that keeps users' saved Sieve filters working after the move to Gridify.
/// Assertions come in pairs: the produced string, and proof that Gridify parses it and selects
/// the rows the Sieve expression meant.
/// </summary>
public class SieveSyntaxTranslatorTest
{
    private sealed class Row
    {
        public int Id { get; init; }
        public string Title { get; init; } = "";
        public int Status { get; init; }
        public string HostName { get; init; } = "";
    }

    private static readonly Row[] Rows =
    [
        new() { Id = 1, Title = "alpha",     Status = 36, HostName = "srv-a" },
        new() { Id = 2, Title = "beta",      Status = 10, HostName = "srv-b" },
        new() { Id = 3, Title = "gamma",     Status = 99, HostName = "web-c" },
        new() { Id = 4, Title = "a,b|c (x)", Status = 99, HostName = "odd-d" },
    ];

    /// <summary>Localized names, exactly as the request-scoped mapper builds them.</summary>
    private static IGridifyMapper<Row> Mapper() => new GridifyMapper<Row>()
        .AddMap("título", r => r.Title)
        .AddMap("id", r => r.Id)
        .AddMap("estado", r => r.Status)
        .AddMap("nome", r => r.HostName);

    private static int[] Apply(string? sieveFilter) =>
        Rows.AsQueryable()
            .ApplyFiltering(SieveSyntaxTranslator.TranslateFilter(sieveFilter), Mapper())
            .Select(r => r.Id)
            .OrderBy(id => id)
            .ToArray();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_filter_becomes_null(string? input) =>
        Assert.Null(SieveSyntaxTranslator.TranslateFilter(input));

    [Fact]
    public void Equals_loses_the_second_sign()
    {
        Assert.Equal("id=2", SieveSyntaxTranslator.TranslateFilter("id==2"));
        Assert.Equal([2], Apply("id==2"));
    }

    [Fact]
    public void Contains_becomes_a_star()
    {
        Assert.Equal("nome=*srv", SieveSyntaxTranslator.TranslateFilter("nome@=srv"));
        Assert.Equal([1, 2], Apply("nome@=srv"));
    }

    /// <summary>
    /// The idiom the REST client hard-codes: `id==1|2|3` means "any of". Gridify has no value
    /// list, so it expands — and must be parenthesised, or a sibling AND binds to the last term.
    /// </summary>
    [Fact]
    public void Value_list_expands_to_a_parenthesised_or()
    {
        Assert.Equal("(id=1|id=2|id=3)", SieveSyntaxTranslator.TranslateFilter("id==1|2|3"));
        Assert.Equal([1, 2, 3], Apply("id==1|2|3"));
    }

    [Fact]
    public void Value_list_stays_atomic_against_a_sibling_and()
    {
        // Without the parentheses this would read as (estado=36) OR (estado=99 AND nome=*srv),
        // which silently returns row 1 only.
        Assert.Equal([1], Apply("estado==36|99,nome@=srv"));
    }

    [Fact]
    public void Multiple_filters_stay_comma_separated()
    {
        Assert.Equal("estado=99,nome=*web", SieveSyntaxTranslator.TranslateFilter("estado==99,nome@=web"));
        Assert.Equal([3], Apply("estado==99,nome@=web"));
    }

    [Theory]
    [InlineData("id!=2", "id!=2")]
    [InlineData("estado>50", "estado>50")]
    [InlineData("estado<50", "estado<50")]
    [InlineData("estado>=99", "estado>=99")]
    [InlineData("estado<=10", "estado<=10")]
    [InlineData("título_=al", "título^al")]
    [InlineData("título!@=al", "título!*al")]
    public void Comparison_operators_map_across(string sieve, string expected) =>
        Assert.Equal(expected, SieveSyntaxTranslator.TranslateFilter(sieve));

    [Theory]
    [InlineData("título@=*ALP", "título=*ALP/i")]
    [InlineData("título==*ALPHA", "título=ALPHA/i")]
    [InlineData("título_=*AL", "título^AL/i")]
    public void Case_insensitive_operators_gain_the_suffix(string sieve, string expected) =>
        Assert.Equal(expected, SieveSyntaxTranslator.TranslateFilter(sieve));

    [Fact]
    public void Case_insensitive_contains_actually_matches()
    {
        Assert.Equal([1], Apply("título@=*ALP"));
    }

    /// <summary>
    /// A value carrying Gridify's own syntax characters must survive as data. Sieve escapes them
    /// with a backslash; the translator unescapes and re-escapes for the new parser.
    /// </summary>
    [Fact]
    public void Reserved_characters_in_a_value_are_re_escaped()
    {
        Assert.Equal([4], Apply(@"título==a\,b\|c (x)"));
    }

    [Fact]
    public void Sort_translates_the_descending_marker()
    {
        Assert.Equal("estado desc", SieveSyntaxTranslator.TranslateSort("-estado"));
        Assert.Equal("título", SieveSyntaxTranslator.TranslateSort("título"));
        Assert.Equal("estado desc,título", SieveSyntaxTranslator.TranslateSort("-estado,título"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_sort_becomes_null(string? input) =>
        Assert.Null(SieveSyntaxTranslator.TranslateSort(input));

    [Fact]
    public void Translated_sort_orders_the_rows()
    {
        var ordered = Rows.AsQueryable()
            .ApplyOrdering(SieveSyntaxTranslator.TranslateSort("-estado")!, Mapper())
            .Select(r => r.Status)
            .ToArray();

        Assert.Equal([99, 99, 36, 10], ordered);
    }

    /// <summary>
    /// An unmapped name must still be rejected after translation — the whitelist is the reason
    /// the mapper exists, and a translation layer must not become a way around it.
    /// </summary>
    [Fact]
    public void Unmapped_property_is_still_rejected()
    {
        Assert.ThrowsAny<System.Exception>(() => Apply("password==x"));
    }
}
