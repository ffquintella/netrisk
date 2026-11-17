using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GUIClient.Agents;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T356, review M63 #7: no screen that saves a whole record can send a copy older than an agent's approved write. The
/// decision is <see cref="AgentSaveGuard"/>, tested here; that every such save path consults it before saving is checked
/// on the view-models' source, as this project does not load Avalonia (UI tests are on demand only, S54).
/// </summary>
[TestSubject(typeof(AgentSaveGuard))]
public class AgentScreenGuardTest
{
    // ---- the decision -----------------------------------------------------------------------------------------------

    [Fact]
    public void TestACopyReadBeforeAnAgentsWriteConflictsAndOneReadAfterDoesNot()
    {
        var presence = new AgentPresence();
        var item = new AgentItemRef(AgentItemKinds.Host, 3);
        var read = DateTimeOffset.UnixEpoch;
        var older = new AgentSaveGuard(presence, item, read);

        presence.RecordChange(item, "Claude", new[] { "comment" }, read.AddSeconds(5));
        var newer = new AgentSaveGuard(presence, item, read.AddSeconds(6));

        Assert.Equal(new[] { "comment" }, older.Conflict()!.Fields);
        Assert.Null(newer.Conflict());
        older.Reset(read.AddSeconds(7)); // the screen saved or reloaded: it holds the server's copy again
        Assert.Null(older.Conflict());
        Assert.Null(new AgentSaveGuard(null, item, read).Conflict()); // no agent runtime: nothing to guard against
    }

    [Fact]
    public void TestAnotherItemsChangeIsNoConflict()
    {
        var presence = new AgentPresence();
        var guard = new AgentSaveGuard(presence, new AgentItemRef(AgentItemKinds.Entity, 1), DateTimeOffset.UnixEpoch);

        presence.RecordChange(new AgentItemRef(AgentItemKinds.Entity, 2), "Claude", new[] { "name" }, DateTimeOffset.UnixEpoch.AddSeconds(1));
        presence.RecordChange(new AgentItemRef(AgentItemKinds.Host, 1), "Claude", new[] { "comment" }, DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Null(guard.Conflict());
    }

    // ---- every whole-record save path consults it before it saves ----------------------------------------------------

    /// <summary>(view-model file, item kind its guard is opened for, the save call the guard must precede).</summary>
    public static TheoryData<string, string, string> SavePaths() => new()
    {
        { "ViewModels/EditRiskViewModel.cs", "Risk", "_risksService.SaveRisk(Risk)" },
        { "ViewModels/EditIncidentViewModel.cs", "Incident", "IncidentsService.UpdateAsync(Incident)" },
        { "ViewModels/EditVulnerabilitiesDialogViewModel.cs", "Finding", "VulnerabilitiesService.UpdateAsync(Vulnerability)" },
        { "ViewModels/EditHostDialogViewModel.cs", "Host", "HostsService.UpdateAsync(Host)" },
        { "ViewModels/Entities/EntityFormViewModel.cs", "Entity", "_entitiesService.SaveEntityAsync(dto)" },
        { "ViewModels/EntitiesViewModel.cs", "Entity", "_entitiesService.SaveEntityAsync(updated)" },
        { "ViewModels/Assessments/AssessmentRunDialogViewModel.cs", "AssessmentRun", "AssessmentsService.UpdateAssessmentRun(assessRun)" }
    };

    [Theory]
    [MemberData(nameof(SavePaths))]
    public void TestTheSavePathRefusesACopyOlderThanAnAgentsWrite(string file, string kind, string save)
    {
        var source = Read(file);

        Assert.Matches(new Regex(@"AgentEditGuard\.For(Since)?\(GUIClient\.Agents\.AgentItemKinds\." + kind + @"\b"), source);
        var saves = Occurrences(source, save).ToList();
        Assert.NotEmpty(saves);
        foreach (var at in saves)
        {
            var body = source[MethodStart(source, at)..at];
            Assert.True(body.Contains("ConflictMessage()", StringComparison.Ordinal),
                $"{file}: '{save}' at {at} is not preceded by the agent guard in its method");
        }
    }

    /// <summary>The run viewer never edits the run's comments: it takes the agent's from the server before each save.</summary>
    [Fact]
    public void TestTheRunViewerMergesTheAgentsChangeBeforeEverySaveOfTheRun()
    {
        var source = Read("ViewModels/Assessments/AssessmentRunViewerViewModel.cs");

        Assert.Matches(new Regex(@"AgentEditGuard\.For\(GUIClient\.Agents\.AgentItemKinds\.AssessmentRun\b"), source);
        var saves = Occurrences(source, "AssessmentsService.UpdateAssessmentRun(").ToList();
        Assert.Equal(2, saves.Count);
        foreach (var at in saves)
            Assert.Contains("await MergeAgentChangesAsync();", source[MethodStart(source, at)..at]);
        Assert.Contains("_run.Comments = fresh.Comments;", source);
    }

    /// <summary>The screens that list these items refresh the one an agent changed and show the presence notice.</summary>
    [Theory]
    [InlineData("ViewModels/RiskViewModel.cs", "Views/RiskView.axaml", "Risk")]
    [InlineData("ViewModels/IncidentsViewModel.cs", "Views/IncidentsView.axaml", "Incident")]
    [InlineData("ViewModels/VulnerabilitiesViewModel.cs", "Views/VulnerabilitiesView.axaml", "Finding")]
    [InlineData("ViewModels/HostsViewModel.cs", "Views/HostsView.axaml", "Host")]
    [InlineData("ViewModels/Entities/EntityFormViewModel.cs", "Views/EntityForm.axaml", "Entity")]
    public void TestTheScreenWatchesAgentPresenceAndShowsTheNotice(string viewModel, string view, string kind)
    {
        Assert.Matches(new Regex(@"new GUIClient\.ViewModels\.Agents\.AgentItemWatcher\(GUIClient\.Agents\.AgentItemKinds\." + kind + @"\b"),
            Read(viewModel));
        var markup = Read(view);
        Assert.Contains("AgentWatch.HasNotice", markup);
        Assert.Contains("{Binding AgentWatch.Notice}", markup);
    }

    private static System.Collections.Generic.IEnumerable<int> Occurrences(string text, string value)
    {
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
            yield return at;
    }

    /// <summary>The start of the member declaration enclosing <paramref name="at"/> (class-level indentation).</summary>
    private static int MethodStart(string source, int at)
    {
        var declaration = new Regex(@"\n    (private|public|protected|internal)[^\n;=]*\(", RegexOptions.Compiled);
        var start = 0;
        foreach (Match m in declaration.Matches(source[..at])) start = m.Index;
        return start;
    }

    private static string Read(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");
            if (File.Exists(project))
                return File.ReadAllText(Path.Combine(Path.GetDirectoryName(project)!, relative));
        }
        throw new InvalidOperationException("Could not find GUIClient/GUIClient.csproj from " + AppContext.BaseDirectory);
    }
}
