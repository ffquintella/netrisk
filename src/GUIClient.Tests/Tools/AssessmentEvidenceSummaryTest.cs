using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GUIClient.Tests.Resources;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Assessments;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// GitHub #80 (T297, S44) — the pure half of the run viewer's comment and evidence controls, and the
/// view's contract with it: the controls are offered only where the server would accept the write, a
/// picked file is refused locally with the same limits the server enforces, a server refusal reads as
/// the right message, and every computed message key resolves in all three resource files.
/// </summary>
[TestSubject(typeof(AssessmentEvidenceSummary))]
public class AssessmentEvidenceSummaryTest
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void TestEvidenceIsAttachableOnlyToAnOpenRealRun(bool isPreview, bool isReadOnly, bool expected)
    {
        Assert.Equal(expected, AssessmentEvidenceSummary.CanAttach(isPreview, isReadOnly));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TestACommentIsEditableUnlessTheRunIsSubmitted(bool isReadOnly, bool expected)
    {
        Assert.Equal(expected, AssessmentEvidenceSummary.CanEditComment(isReadOnly));
    }

    [Theory]
    [InlineData(1, 0, null)]
    [InlineData(AssessmentEvidencePolicy.MaxEvidenceBytes, AssessmentEvidencePolicy.MaxEvidencePerAnswer - 1, null)]
    [InlineData(0, 0, AssessmentEvidenceSummary.EmptyKey)]
    [InlineData(AssessmentEvidencePolicy.MaxEvidenceBytes + 1, 0, AssessmentEvidenceSummary.TooLargeKey)]
    [InlineData(1, AssessmentEvidencePolicy.MaxEvidencePerAnswer, AssessmentEvidenceSummary.LimitReachedKey)]
    public void TestAPickedFileIsRefusedLocallyByTheServersLimits(long bytes, int attached, string? expected)
    {
        Assert.Equal(expected, AssessmentEvidenceSummary.UploadRejectionKey(bytes, attached));
    }

    [Theory]
    [InlineData("{\"error\":\"run_submitted\"}", AssessmentEvidenceSummary.RunSubmittedKey)]
    [InlineData("{\"error\":\"evidence_limit_reached\"}", AssessmentEvidenceSummary.LimitReachedKey)]
    [InlineData("{\"error\":\"invalid_parameter\"}", AssessmentEvidenceSummary.UploadFailedKey)]
    [InlineData("", AssessmentEvidenceSummary.UploadFailedKey)]
    [InlineData(null, AssessmentEvidenceSummary.UploadFailedKey)]
    public void TestAnUploadRefusalReadsAsItsMessage(string? server, string expected)
    {
        Assert.Equal(expected, AssessmentEvidenceSummary.FailureKey(server));
    }

    [Theory]
    [InlineData("{\"error\":\"not_the_uploader\"}", AssessmentEvidenceSummary.NotUploaderKey)]
    [InlineData("{\"error\":\"run_submitted\"}", AssessmentEvidenceSummary.RunSubmittedKey)]
    [InlineData("boom", AssessmentEvidenceSummary.DeleteFailedKey)]
    [InlineData(null, AssessmentEvidenceSummary.DeleteFailedKey)]
    public void TestADeleteRefusalReadsAsItsMessage(string? server, string expected)
    {
        Assert.Equal(expected, AssessmentEvidenceSummary.DeleteFailureKey(server));
    }

    [Theory]
    [InlineData(null, "application/octet-stream")]
    [InlineData("", "application/octet-stream")]
    [InlineData("image/png", "image/png")]
    public void TestTheIconTypeIsNeverEmpty(string? type, string expected)
    {
        Assert.Equal(expected, AssessmentEvidenceSummary.IconType(type));
    }

    /// <summary>Every key the viewer computes exists in every shipped culture.</summary>
    [Theory]
    [InlineData("Localization.resx")]
    [InlineData("Localization.en-US.resx")]
    [InlineData("Localization.pt-BR.resx")]
    public void TestEveryComputedKeyResolvesIn(string file)
    {
        var declared = XDocument.Load(EntityConfigurationSchema.ResourcePath(file)).Root!
            .Elements("data")
            .Select(d => d.Attribute("name")?.Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = AssessmentEvidenceSummary.MessageKeys.Where(k => !declared.Contains(k)).ToList();

        Assert.True(missing.Count == 0, $"{file} does not declare: {string.Join(", ", missing)}");
    }

    // --- the view's contract -----------------------------------------------------------------------

    private static string Source(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "GUIClient", relative);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }

        throw new InvalidOperationException($"Could not find GUIClient/{relative} walking up from {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// The viewer's evidence controls are bound to the switches above — so a submitted run shows neither
    /// the attach nor the delete button, and the comment box is read-only — and every string the question
    /// card binds is a property the viewer declares.
    /// </summary>
    [Fact]
    public void TestTheRunViewerBindsTheControlsToTheirSwitches()
    {
        var view = Source(Path.Combine("Views", "Assessments", "AssessmentRunViewer.axaml"));
        var viewModel = Source(Path.Combine("ViewModels", "Assessments", "AssessmentRunViewerViewModel.cs"));

        Assert.Contains("IsVisible=\"{Binding CanAttachEvidence}\"", view);
        Assert.Contains("IsVisible=\"{Binding CanDelete}\"", view);
        Assert.Contains("IsReadOnly=\"{Binding !CanEditComment}\"", view);
        Assert.Contains("MaxLength=\"{Binding CommentMaxLength}\"", view);

        var strings = Regex.Matches(view, @"DataContext\.(?<name>Str\w+)")
            .Select(m => m.Groups["name"].Value).Distinct().ToList();
        Assert.Contains("StrComment", strings);
        Assert.Contains("StrAttachEvidence", strings);
        Assert.All(strings, name =>
            Assert.Matches(new Regex($@"public\s+string\s+{name}\s*=>"), viewModel));
    }
}
