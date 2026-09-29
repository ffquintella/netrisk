using System.Collections.Generic;
using GUIClient.Tools;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// The item the issue-tracker Provider ComboBox shows for the draft's provider. The regression is the
/// refilled list: the answer must be the new instance, since that is what makes Avalonia re-select.
/// </summary>
[TestSubject(typeof(ComboSelection))]
public class ComboSelectionTest
{
    private sealed record Option(int Kind, string Name);

    [Fact]
    public void ReturnsTheOptionWhoseKeyMatches()
    {
        var options = new List<Option> { new(1, "Jira"), new(5, "JiraDataCenter") };

        var item = ComboSelection.ItemFor(options, o => o.Kind, 5);

        Assert.Same(options[1], item);
    }

    [Fact]
    public void AfterTheListIsRefilledReturnsTheNewInstance()
    {
        var options = new List<Option> { new(5, "JiraDataCenter") };
        var before = ComboSelection.ItemFor(options, o => o.Kind, 5);

        options.Clear();
        options.Add(new Option(5, "JiraDataCenter"));
        var after = ComboSelection.ItemFor(options, o => o.Kind, 5);

        Assert.NotNull(after);
        Assert.Same(options[0], after);
        Assert.NotSame(before, after);
    }

    [Fact]
    public void ReturnsNullWhileTheListIsEmpty()
    {
        Assert.Null(ComboSelection.ItemFor(new List<Option>(), o => o.Kind, 5));
    }

    [Fact]
    public void ReturnsNullWhenNoOptionMatches()
    {
        var options = new List<Option> { new(1, "Jira") };

        Assert.Null(ComboSelection.ItemFor(options, o => o.Kind, 5));
    }
}
