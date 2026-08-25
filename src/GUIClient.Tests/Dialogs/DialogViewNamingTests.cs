using System;
using GUIClient.ViewModels.Dialogs;
using Xunit;

namespace GUIClient.Tests.Dialogs;

public class DialogViewNamingTests
{
    [Fact]
    public void StripsTheViewModelSuffixAndOffersTheBareStemFirst()
    {
        var candidates = DialogViewNaming.GetCandidateViewNames("EditEntityDialogViewModel");

        // The bare stem must come first so the `*Dialog` views that already resolved keep
        // resolving to exactly the same type.
        Assert.Equal("EditEntityDialog", candidates[0]);
    }

    [Theory]
    // The seven dialogs whose view is named `*Window` while the view model is not. Every one of
    // these threw "View for … was not found!" and aborted the process when opened.
    [InlineData("EditRiskViewModel", "EditRiskWindow")]
    [InlineData("CloseRiskViewModel", "CloseRiskWindow")]
    [InlineData("EditMitigationViewModel", "EditMitigationWindow")]
    [InlineData("EditIncidentViewModel", "EditIncidentWindow")]
    [InlineData("IncidentResponsePlanViewModel", "IncidentResponsePlanWindow")]
    [InlineData("IncidentResponsePlanTaskViewModel", "IncidentResponsePlanTaskWindow")]
    [InlineData("VulnerabilityImportViewModel", "VulnerabilityImportWindow")]
    public void OffersTheWindowSuffixedViewName(string viewModelName, string expectedViewName)
    {
        Assert.Contains(expectedViewName, DialogViewNaming.GetCandidateViewNames(viewModelName));
    }

    [Theory]
    // The `*Dialog` pairings, which the old single-name lookup already handled.
    [InlineData("AssessmentImportDialogViewModel", "AssessmentImportDialog")]
    [InlineData("FixRequestDialogViewModel", "FixRequestDialog")]
    [InlineData("AddFaceImageViewModel", "AddFaceImage")]
    [InlineData("EditMgmtReviewViewModel", "EditMgmtReview")]
    public void KeepsResolvingTheNamesThatAlreadyWorked(string viewModelName, string expectedViewName)
    {
        Assert.Contains(expectedViewName, DialogViewNaming.GetCandidateViewNames(viewModelName));
    }

    [Fact]
    public void TrimsTheSuffixOnlyFromTheEnd()
    {
        // The previous implementation used Replace("ViewModel", ""), which would strip the
        // substring from the middle of a name too.
        var candidates = DialogViewNaming.GetCandidateViewNames("ViewModelPickerViewModel");

        Assert.Equal("ViewModelPicker", candidates[0]);
    }

    [Fact]
    public void AcceptsANameThatIsAlreadyAStem()
    {
        Assert.Equal("EditRisk", DialogViewNaming.GetCandidateViewNames("EditRisk")[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RejectsAMissingName(string? viewModelName)
    {
        Assert.Throws<ArgumentException>(() => DialogViewNaming.GetCandidateViewNames(viewModelName!));
    }

    [Fact]
    public void RejectsANameThatIsNothingButTheSuffix()
    {
        Assert.Throws<ArgumentException>(() => DialogViewNaming.GetCandidateViewNames("ViewModel"));
    }
}
