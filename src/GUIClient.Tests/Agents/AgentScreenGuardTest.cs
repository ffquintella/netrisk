using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        { "ViewModels/EditHostDialogViewModel.cs", "Host", "HostsService.UpdateHostAsync(Host)" },
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

    // ---- T356 remainder: each changed control is highlighted until the user marks it as seen or edits the item -------

    private static readonly AgentItemRef RiskOne = new(AgentItemKinds.Risk, 1);

    [Fact]
    public void TestAnAgentsChangesAccumulateAndStayUntilTheUserMarksThemAsSeen()
    {
        var presence = new AgentPresence();
        var t = DateTimeOffset.UnixEpoch;

        presence.RecordChange(RiskOne, "Claude", new[] { "notes" }, t);
        presence.RecordChange(RiskOne, "Claude", new[] { "subject", "notes" }, t.AddMinutes(30)); // long after the 2-min window

        Assert.Equal(new[] { "notes", "subject" }, presence.Unacknowledged(RiskOne)!.Fields);
        Assert.Null(presence.Unacknowledged(new AgentItemRef(AgentItemKinds.Risk, 2)));
        Assert.Null(presence.Unacknowledged(new AgentItemRef(AgentItemKinds.Incident, 1)));

        presence.Acknowledge(RiskOne);
        Assert.Null(presence.Unacknowledged(RiskOne));
        Assert.NotNull(presence.ChangedSince(RiskOne, t)); // the save guard is unaffected by acknowledging
    }

    [Fact]
    public void TestEditingAFieldEndsItsHighlightAndSavingTheItemEndsEveryOne()
    {
        var presence = new AgentPresence();
        var raised = 0;
        presence.RecordChange(RiskOne, "Claude", new[] { "notes", "subject" }, DateTimeOffset.UnixEpoch);
        presence.Changed += () => raised++;

        presence.UserEdited(RiskOne, new[] { "notes" });
        Assert.Equal(new[] { "subject" }, presence.Unacknowledged(RiskOne)!.Fields);
        presence.UserEdited(RiskOne, new[] { "status" }); // a field the agent did not change: nothing to end
        presence.UserEdited(RiskOne);                       // the user saved the whole item
        Assert.Null(presence.Unacknowledged(RiskOne));
        Assert.Equal(2, raised);

        presence.RecordChange(RiskOne, "Claude", new[] { "notes" }, DateTimeOffset.UnixEpoch);
        presence.Clear();                                   // sign-out, server stopped
        Assert.Null(presence.Unacknowledged(RiskOne));
    }

    [Fact]
    public void TestAScreenLightsTheControlsOfTheChangedFieldsAndNamesTheOthers()
    {
        var change = new AgentItemChange(new AgentItemRef(AgentItemKinds.Incident, 4), "Claude", DateTimeOffset.UnixEpoch,
            new[] { "description", "timeline_entry", "mystery_field" });

        var lit = AgentHighlightScreens.Incidents.StateFor(change, "Changed by Claude");

        Assert.Equal(new[] { "Description" }, lit.Controls);
        Assert.True(lit.IsLit("Description"));
        Assert.False(lit.IsLit("Notes"));
        Assert.Equal("Changed by Claude", lit.TipFor("Description"));
        Assert.Null(lit.TipFor("Notes"));                       // no tooltip on a control that is not highlighted
        // A field the screen has no control for is named, never dropped — an unknown one included (fail visible).
        Assert.Equal(new[] { "timeline_entry", "mystery_field" }, lit.NotShown);
        Assert.Equal(new[] { "description", "timeline_entry", "mystery_field" }, lit.Fields);
        Assert.Same(AgentFieldHighlights.None, AgentHighlightScreens.Risks.StateFor(change, "x")); // another kind
        Assert.Same(AgentFieldHighlights.None, AgentHighlightScreens.Incidents.StateFor(null, null));
    }

    /// <summary>Every argument each edit tool can take, so its plan names every field it can write.</summary>
    private static JsonObject Everything(string tool, AgentEditFixtures f)
    {
        var arguments = f.ArgumentsFor(tool);
        string[] texts = tool switch
        {
            "update_risk" => new[] { "subject", "notes", "scenario_cause", "scenario_vulnerability", "scenario_central_event", "scenario_consequences" },
            "create_risk" => new[] { "notes", "scenario_cause", "scenario_vulnerability", "scenario_central_event", "scenario_consequences" },
            "update_incident" or "create_incident" => new[] { "description", "report", "impact", "cause", "solution", "recommendations", "notes" },
            "update_entity_fields" => new[] { "name", "description", "objective", "technology" },
            "update_host_fields" => new[] { "comment", "os" },
            _ => Array.Empty<string>()
        };
        foreach (var name in texts) arguments[name] = "Agent text for " + name;
        return arguments;
    }

    [Theory]
    [MemberData(nameof(EditTools))]
    public async Task TestEveryFieldAnEditToolWritesIsShownOrNamedOnTheScreenOfItsKind(string tool)
    {
        var f = new AgentEditFixtures();
        f.Run.AnalystId = 5; // so comment_assessment_run also names the analyst line the server writes
        var prompter = new ScriptedPrompter { OnWrite = (_, _) => Task.FromResult(true) };
        var rig = AgentReadRig.OverAllFixtures(edits: f, prompter: prompter);

        var answer = await rig.CallAsync(tool, Everything(tool, f));

        Assert.False(answer.IsError, answer.Body);
        var prompt = Assert.Single(prompter.WritePrompts);
        var screen = AgentHighlightScreens.ForKind(prompt.Item.Kind);
        Assert.NotNull(screen);
        var uncovered = prompt.Changes.Select(c => c.Field).Where(field => !screen!.Covers(field)).ToList();
        Assert.True(uncovered.Count == 0, $"{tool} writes {string.Join(", ", uncovered)}, which screen '{screen!.Name}' neither shows nor names");
        // And what landed is what the screen will highlight (T356 reads presence).
        var item = prompt.Item.Id == 0 ? prompt.Item with { Id = (int)answer.Data!["item_id"]! } : prompt.Item;
        Assert.Equal(prompt.Changes.Select(c => c.Field), rig.Rig.Presence.Unacknowledged(item)!.Fields);
    }

    public static TheoryData<string> EditTools() => AgentEditFixtures.AllTools();

    /// <summary>(screen, view) for the list screens whose detail panel binds the watcher's highlights by control id.</summary>
    public static TheoryData<string, string> HighlightedViews() => new()
    {
        { "risks", "Views/RiskView.axaml" },
        { "incidents", "Views/IncidentsView.axaml" },
        { "findings", "Views/VulnerabilitiesView.axaml" },
        { "hosts", "Views/HostsView.axaml" }
    };

    [Theory]
    [MemberData(nameof(HighlightedViews))]
    public void TestEveryMappedControlIsHighlightedWithATooltipAndTheNoticeOffersMarkAsSeen(string screenName, string view)
    {
        var screen = AgentHighlightScreens.All.Single(x => x.Name == screenName);
        var markup = Read(view);

        foreach (var control in screen.Controls.Values.Distinct())
        {
            Assert.Matches(new Regex(@"Classes\.agentChanged=""\{Binding AgentWatch\.Highlights, Converter=\{x:Static \w+:AgentHighlightConverter\.IsLit\}, ConverterParameter=" + control + @","), markup);
            Assert.Matches(new Regex(@"ToolTip\.Tip=""\{Binding AgentWatch\.Highlights, Converter=\{x:Static \w+:AgentHighlightConverter\.Tip\}, ConverterParameter=" + control + @"\}"), markup);
        }
        Assert.Contains("{Binding AgentWatch.BtAcknowledgeClicked}", markup);
        Assert.Contains("{Binding AgentWatch.HasHighlights, FallbackValue=False}", markup);
        Assert.Contains("{Binding AgentWatch.StrAcknowledge}", markup);
    }

    [Fact]
    public void TestTheEntityFormAndTheRunDialogHighlightTheirFieldsAndEndItWhenTheUserEdits()
    {
        // The entity form lights a field by its property key — the map is the identity.
        Assert.All(AgentHighlightScreens.EntityForm.Controls, p => Assert.Equal(p.Key, p.Value));
        Assert.Contains(@"Classes.agentChanged=""{Binding IsAgentChanged}"" ToolTip.Tip=""{Binding AgentChangedTip}""", Read("Views/EntityForm.axaml"));
        var form = Read("ViewModels/Entities/EntityFormViewModel.cs");
        Assert.Contains("field.IsAgentChanged = highlights.IsLit(field.PropertyKey);", form);
        Assert.Contains("AgentWatch?.UserEdited(edited.PropertyKey);", form);
        Assert.Contains("if (edited) UserEdited?.Invoke(this);", Read("ViewModels/Entities/EntityFieldViewModel.cs"));

        var dialog = Read("ViewModels/Assessments/AssessmentRunDialogViewModel.cs");
        Assert.Contains("AgentHighlightScreens.AssessmentRunDialog.StateFor(", dialog);
        Assert.Contains(@"CommentsAgentChanged = highlights.IsLit(""Comments"");", dialog);
        Assert.Contains(@"_agentGuard?.UserEdited(""comments"");", dialog);
        Assert.Contains(@"Classes.agentChanged=""{Binding CommentsAgentChanged}""", Read("Views/Assessments/AssessmentRunDialog.axaml"));
        Assert.Equal("Comments", AgentHighlightScreens.AssessmentRunDialog.Controls["comments"]);
    }

    [Fact]
    public void TestTheHighlightIsAStyleClassOfThemeTokensAndNeverColourAlone()
    {
        var styles = Read("Styles/WindowStyles.axaml");
        foreach (var selector in new[] { "TextBlock.agentChanged", "TextBox.agentChanged" })
        {
            var at = styles.IndexOf($@"<Style Selector=""{selector}"">", StringComparison.Ordinal);
            Assert.True(at >= 0, selector + " is not defined in WindowStyles.axaml");
            var body = styles[at..styles.IndexOf("</Style>", at, StringComparison.Ordinal)];
            Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{3,8}"), body);
            Assert.Contains("DynamicResource", body);
        }
        // Beyond the colour: an underline on a value, and the tooltip text the watcher builds from a localized string.
        Assert.Contains(@"<Setter Property=""TextDecorations"" Value=""Underline""/>", styles);
        var watcher = Read("ViewModels/Agents/AgentItemWatcher.cs");
        Assert.Contains(@"Localizer[""AgentChangedFieldTip""]", watcher);
        Assert.Contains(@"Localizer[""AgentHighlightedItemMSG""]", watcher);
        Assert.Contains(@"Localizer[""AgentAcknowledgeChanges""]", watcher);
    }

    /// <summary>(view-model file, the save call after which the user's save ends the agent highlights of the item).</summary>
    public static TheoryData<string, string> SavedPaths() => new()
    {
        { "ViewModels/EditRiskViewModel.cs", "_risksService.SaveRisk(Risk);" },
        { "ViewModels/EditIncidentViewModel.cs", "await IncidentsService.UpdateAsync(Incident);" },
        { "ViewModels/EditVulnerabilitiesDialogViewModel.cs", "await VulnerabilitiesService.UpdateAsync(Vulnerability);" },
        { "ViewModels/Entities/EntityFormViewModel.cs", "_entity = result;" }
    };

    [Theory]
    [MemberData(nameof(SavedPaths))]
    public void TestAUsersSaveOfTheItemEndsItsHighlights(string file, string save)
    {
        var source = Read(file);
        var at = source.IndexOf(save, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{file}: '{save}' not found");
        Assert.Equal(at, source.LastIndexOf(save, StringComparison.Ordinal)); // the one save path of the item
        var following = source[(at + save.Length)..Math.Min(source.Length, at + save.Length + 160)];
        Assert.Contains("_agentGuard?.Saved();", following);
    }

    // ---- T356 review: appends never make a form's copy stale; a failed save never ends a highlight -----------------

    [Theory]
    [InlineData("add_incident_timeline_entry", AgentItemKinds.Incident)]
    [InlineData("add_finding_note", AgentItemKinds.Finding)]
    public async Task TestAnAppendIsShownButNeverMakesAnOpenFormsCopyStale(string tool, string kind)
    {
        // The form read its copy before the agent's append; the append leaves the record and its version untouched.
        var rig = AgentReadRig.OverAllFixtures(edits: new AgentEditFixtures());
        var item = new AgentItemRef(kind, 1);
        var guard = new AgentSaveGuard(rig.Rig.Presence, item, rig.Rig.Time.GetUtcNow().AddSeconds(-1));

        var answer = await rig.CallAsync(tool);

        Assert.False(answer.IsError, answer.Body);
        Assert.Null(guard.Conflict());                                       // the user's save goes through
        Assert.NotNull(rig.Rig.Presence.Unacknowledged(item));               // still named and highlighted
        Assert.True(rig.Rig.Presence.PresenceOf(item, rig.Rig.Time.GetUtcNow())!.Editing);
    }

    [Theory]
    [InlineData("update_incident", AgentItemKinds.Incident)]
    [InlineData("update_finding_assignment", AgentItemKinds.Finding)]
    public async Task TestAnEditOfTheRecordItselfStillMakesAnOpenFormsCopyStale(string tool, string kind)
    {
        var rig = AgentReadRig.OverAllFixtures(edits: new AgentEditFixtures());
        var item = new AgentItemRef(kind, 1);
        var guard = new AgentSaveGuard(rig.Rig.Presence, item, rig.Rig.Time.GetUtcNow().AddSeconds(-1));

        Assert.False((await rig.CallAsync(tool)).IsError);

        Assert.NotNull(guard.Conflict());
    }

    [Fact]
    public async Task TestAFailedSaveKeepsTheAgentsHighlightAndTheGuard()
    {
        var presence = new AgentPresence();
        var t = DateTimeOffset.UnixEpoch;
        presence.RecordChange(RiskOne, "Claude", new[] { "notes" }, t);
        var guard = new AgentSaveGuard(presence, RiskOne, t.AddSeconds(-1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.SaveAsync(() => throw new InvalidOperationException("refused by the server"), () => t.AddSeconds(5)));
        Assert.False(await guard.SaveAsync(() => Task.FromResult(false), () => t.AddSeconds(5)));
        Assert.False(guard.Save(() => false, () => t.AddSeconds(5)));
        Assert.Equal(new[] { "notes" }, presence.Unacknowledged(RiskOne)!.Fields);
        Assert.NotNull(guard.Conflict());

        Assert.True(guard.Save(() => true, () => t.AddSeconds(5)));
        Assert.Null(presence.Unacknowledged(RiskOne));
        Assert.Null(guard.Conflict());
    }

    [Fact]
    public void TestTheHostAndRunDialogsEndTheHighlightOnlyAfterAConfirmedSave()
    {
        // The host PUT is the awaited one (UpdateAsync is async void: its failure was never seen), inside the guard's save.
        var host = Read("ViewModels/EditHostDialogViewModel.cs");
        Assert.DoesNotContain("HostsService.UpdateAsync(Host)", host);
        Assert.Contains("await GUIClient.ViewModels.Agents.AgentEditGuard.SaveAsync(_agentGuard, async () =>", host);
        Assert.Contains("await HostsService.UpdateHostAsync(Host);", host);
        Assert.DoesNotContain("_agentGuard?.Saved()", host);

        // The run update now reports whether the server took it; the dialog stays open and says so when it did not.
        var run = Read("ViewModels/Assessments/AssessmentRunDialogViewModel.cs");
        Assert.Contains("if (!GUIClient.ViewModels.Agents.AgentEditGuard.Save(_agentGuard,", run);
        Assert.Contains("() => AssessmentsService.UpdateAssessmentRun(assessRun)))", run);
        Assert.Contains(@"Toasts.Error(Localizer[""ErrorSavingAssessmentRunMSG""]);", run);
        Assert.DoesNotContain("_agentGuard?.Saved()", run);
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
