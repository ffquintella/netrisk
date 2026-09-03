using GUIClient.Tools;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// Whether pressing *Preview* on an issue-tracker connection has to save first
/// (Track 4 milestone 4.6).
///
/// Worth its own test because it decides whether a read-looking button performs a **write**. Both
/// directions are defects an operator would notice and misdiagnose: too eager and every preview is a
/// silent save of a form they may have been experimenting in; too lazy and the preview shows the old
/// template, so a placeholder edit looks like it did nothing and gets reverted as broken.
/// </summary>
[TestSubject(typeof(IssueTemplateDraft))]
public class IssueTemplateDraftTest
{
    [Fact]
    public void AnUntouchedFormNeedsNoSave()
    {
        Assert.False(IssueTemplateDraft.AnyChanged(
            ("[{{Severity}}] {{Title}}", "[{{Severity}}] {{Title}}"),
            ("body", "body"),
            ("{\"4\":\"Highest\"}", "{\"4\":\"Highest\"}"),
            ("security", "security")));
    }

    [Fact]
    public void AnyOneEditedFieldNeedsASave()
    {
        Assert.True(IssueTemplateDraft.AnyChanged(("a", "b")));

        Assert.True(IssueTemplateDraft.AnyChanged(
            ("same", "same"),
            ("body", "body edited"),
            ("same", "same")));
    }

    /// <summary>
    /// Null and empty are the same value.
    ///
    /// The editor writes <c>""</c> into a text box the server returned null for, so treating them as
    /// different would make *every* preview a save — which is exactly what this predicate exists to
    /// prevent, and would be invisible until somebody noticed their connection's audit trail filling
    /// up with saves they never made.
    /// </summary>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void NullAndEmptyAreTheSameValue(string? stored, string? draft)
    {
        Assert.False(IssueTemplateDraft.Changed(stored, draft));
    }

    [Theory]
    [InlineData(null, "[{{Severity}}] {{Title}}")]
    [InlineData("[{{Severity}}] {{Title}}", null)]
    public void SettingOrClearingATemplateIsAChange(string? stored, string? draft)
    {
        Assert.True(IssueTemplateDraft.Changed(stored, draft));
    }

    /// <summary>
    /// Whitespace and case count as edits.
    ///
    /// A trailing newline is part of what a template renders, and a placeholder's case is something an
    /// operator is entitled to see the preview refresh for even though the substitution itself is
    /// case-insensitive — a trimming or case-folding comparison would leave them staring at an
    /// unchanged preview after a real edit.
    /// </summary>
    [Theory]
    [InlineData("body", "body\n")]
    [InlineData("body", " body")]
    [InlineData("{{Severity}}", "{{severity}}")]
    public void WhitespaceAndCaseAreChanges(string stored, string draft)
    {
        Assert.True(IssueTemplateDraft.Changed(stored, draft));
    }

    [Fact]
    public void NoFieldsMeansNothingToSave()
    {
        // The degenerate call must not report a change: a caller with nothing to compare has nothing
        // to save, and returning true would make Preview write on a connection it never read.
        Assert.False(IssueTemplateDraft.AnyChanged());
    }
}
