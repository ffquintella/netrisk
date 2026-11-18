using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DAL.Entities;
using GUIClient.Tools.RiskIndex;
using Model.Entities;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// M55, T263/T264 (S40 §6.5, §6.6, §6.7): the source catalogue the Sources tab edits, the connection default-entity choices,
/// and the Risk context tab's pending batch.
/// </summary>
public class RiskSourcesAndContextTest
{
    // ---------- sources (T263) ----------

    [Fact]
    public void TestTheCatalogueListsEverySourceOfTheModelOnce()
    {
        var model = typeof(RiskIndexSources).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();

        Assert.Equal(model.OrderBy(s => s), RiskSourceCatalogue.All.Select(s => s.Id).OrderBy(s => s));
        Assert.Equal(RiskSourceCatalogue.All.Count, RiskSourceCatalogue.All.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void TestEveryDefaultEnabledSourceIsAvailableAndEveryWindowedSourceHasADefault()
    {
        foreach (var id in RiskIndexSources.DefaultEnabled())
            Assert.NotEqual(RiskSourceAvailability.NotAvailable, RiskSourceCatalogue.Find(id)!.Availability);
        foreach (var id in RiskIndexSources.DefaultFreshnessDays().Keys)
            Assert.True(RiskSourceCatalogue.Find(id)!.HasFreshnessWindow, id);
        Assert.All(RiskSourceCatalogue.All.Where(s => s.Availability == RiskSourceAvailability.NotAvailable),
            s => Assert.False(string.IsNullOrWhiteSpace(s.Qualifier)));
    }

    [Fact]
    public void TestANotBuiltSourceCannotBeSwitchedOnButCanBeSwitchedOff()
    {
        var notBuilt = RiskSourceCatalogue.Find(RiskIndexSources.CmdbRelations)!;
        var vision = RiskSourceCatalogue.Find(RiskIndexSources.V1Device)!;

        Assert.Equal("RiskSourceRequiresTask", RiskSourceCatalogue.Toggle(notBuilt, false, true).ReasonKey);
        Assert.True(RiskSourceCatalogue.Toggle(notBuilt, true, true).Enabled);
        Assert.True(RiskSourceCatalogue.Toggle(vision, true, true).Enabled);
        Assert.Equal(RiskIndexAdminAccess.RequiresConfigure, RiskSourceCatalogue.Toggle(vision, true, false).ReasonKey);
    }

    /// <summary>
    /// M58, T291: the Jira Assets import writes the host's internet exposure, so CMDB-INET may be enabled; the relations it
    /// imports wait for the full-mode drill-down that reads them (T284), so CMDB-REL still cannot be switched on.
    /// </summary>
    [Fact]
    public void TestCmdbInternetFacingCanBeSwitchedOnAndCmdbRelationsAwaitsT284()
    {
        var inet = RiskSourceCatalogue.Find(RiskIndexSources.CmdbInternetFacing)!;
        var relations = RiskSourceCatalogue.Find(RiskIndexSources.CmdbRelations)!;

        Assert.Equal(RiskSourceAvailability.Available, inet.Availability);
        Assert.True(RiskSourceCatalogue.Toggle(inet, false, true).Enabled);
        Assert.Equal((RiskSourceAvailability.NotAvailable, "T284"), (relations.Availability, relations.Qualifier));
    }

    /// <summary>M56, T266–T267: the Tenable connector exists, so TEN-VM, TEN-AES and TEN-ACR may be enabled in a draft.</summary>
    [Theory]
    [InlineData(RiskIndexSources.TenableVm)]
    [InlineData(RiskIndexSources.TenableAes)]
    [InlineData(RiskIndexSources.TenableAcr)]
    public void TestTheTenableSourcesCanBeSwitchedOn(string source)
    {
        var info = RiskSourceCatalogue.Find(source)!;

        Assert.Equal(RiskSourceAvailability.Available, info.Availability);
        Assert.True(RiskSourceCatalogue.Toggle(info, false, true).Enabled);
    }

    /// <summary>M57, T271–T272: the Vision One posture and attack-surface sources are collected and scored, so may be enabled.</summary>
    [Theory]
    [InlineData(RiskIndexSources.V1InternetFacing)]
    [InlineData(RiskIndexSources.V1Accounts)]
    [InlineData(RiskIndexSources.V1Cloud)]
    [InlineData(RiskIndexSources.V1LocalApps)]
    [InlineData(RiskIndexSources.V1SecurityConfiguration)]
    public void TestTheVisionOnePostureSourcesCanBeSwitchedOn(string source)
    {
        var info = RiskSourceCatalogue.Find(source)!;

        Assert.Equal(RiskSourceAvailability.Available, info.Availability);
        Assert.True(RiskSourceCatalogue.Toggle(info, false, true).Enabled);
    }

    [Fact]
    public void TestTogglingASourceOffAndOnAgainIsNoChange()
    {
        var draft = new RiskIndexParameters();
        var off = RiskSourceCatalogue.WithEnabled(draft, RiskIndexSources.SscTotal, false);
        var on = RiskSourceCatalogue.WithEnabled(off, RiskIndexSources.SscTotal, true);

        Assert.DoesNotContain(RiskIndexSources.SscTotal, off.EnabledSources);
        Assert.Equal(["fontes_habilitadas"], RiskIndexProfileRules.ChangedKeys(draft, off));
        Assert.Empty(RiskIndexProfileRules.ChangedKeys(draft, on));
    }

    [Fact]
    public void TestAWindowEditChangesOnlyThatSourceAndTheInventoryRuleSeesIt()
    {
        var draft = RiskSourceCatalogue.WithWindow(new RiskIndexParameters(), RiskIndexSources.TenableNessus, 300);

        Assert.Equal(300, draft.FreshnessDays[RiskIndexSources.TenableNessus]);
        Assert.Equal(7, draft.FreshnessDays[RiskIndexSources.V1Device]);
        var issues = RiskIndexProfileRules.Validate(DAL.Enums.RiskIndexMode.Interim, draft, null, null, null).Errors;
        Assert.Contains(issues, i => i.Code == "inventory_window_too_short");
    }

    [Fact]
    public void TestTheLinesReadTheActiveAndTheDraftSideBySide()
    {
        var active = new RiskIndexParameters();
        var draft = RiskSourceCatalogue.WithWindow(RiskSourceCatalogue.WithEnabled(active, RiskIndexSources.V1Cve, false),
            RiskIndexSources.V1Cve, 14);

        var line = RiskSourceCatalogue.Lines(active, draft).Single(l => l.Source.Id == RiskIndexSources.V1Cve);
        Assert.True(line.EnabledActive);
        Assert.False(line.EnabledDraft);
        Assert.Equal(7, line.WindowActive);
        Assert.Equal(14, line.WindowDraft);

        var noActive = RiskSourceCatalogue.Lines(null, draft).Single(l => l.Source.Id == RiskIndexSources.V1Cve);
        Assert.False(noActive.EnabledActive);
        Assert.Null(noActive.WindowActive);
    }

    [Fact]
    public void TestAConnectionDefaultsOnlyToAnEntityThatOwnsHosts()
    {
        Entity E(int id, string type, string name) => new()
        {
            Id = id, DefinitionName = type, DefinitionVersion = "1", Status = "active",
            EntitiesProperties = [new EntitiesProperty { Type = "name", Value = name, Name = name }]
        };

        var choices = ConnectionEntityChoices.Filter([E(1, "person", "Ana"), E(2, "organizationUnit", "Unidade B"),
            E(3, "application", "App A"), E(4, "securityClassificationLevel", "Restrita")]);

        Assert.Equal([3, 2], choices.Select(e => e.Id));
    }

    // ---------- risk context (T264) ----------

    private static EntityRiskContextRowDto Process(int id = 10, int? criticality = 3) =>
        new(id, "P-Matrícula", "businessProcess", 1, true, criticality, false, null, false, null, null, null);

    private static EntityRiskContextRowDto App(int id = 20) =>
        new(id, "APP-03", "application", 1, true, 5, true, true, true, 51, null, null);

    private static RiskContextDraft Loaded()
    {
        var draft = new RiskContextDraft();
        draft.Load([Process(), App()]);
        return draft;
    }

    [Fact]
    public void TestAnEditBackToTheStoredValuesIsNoChange()
    {
        var draft = Loaded();
        draft.Set(10, new RiskContextValues(5, null, null));
        Assert.True(draft.IsChanged(10));
        Assert.Equal(1, draft.ChangedCount);

        draft.Set(10, new RiskContextValues(3, null, null));
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void TestAFieldTheTypeDoesNotSupportIsNeverSent()
    {
        var draft = Loaded();
        // A process has no internet exposure nor classification (S39 §5.2/§5.3).
        draft.Set(10, new RiskContextValues(4, true, 51));

        var (request, reason) = draft.Batch("Revisão do BIA preliminar com a Reitoria");
        Assert.Null(reason);
        var change = Assert.Single(request!.Changes);
        Assert.Equal(new EntityRiskContextChange(10, 4, null, null), change);
    }

    [Fact]
    public void TestClearingAValueSendsNullAsNotDeclared()
    {
        var draft = Loaded();
        draft.Set(20, new RiskContextValues(null, false, null));

        var (request, _) = draft.Batch("Aplicação desativada para a internet");
        Assert.Equal(new EntityRiskContextChange(20, null, false, null), Assert.Single(request!.Changes));
    }

    [Fact]
    public void TestTheBatchNeedsChangesAndAJustificationOfTwentyCharacters()
    {
        var draft = Loaded();
        Assert.Equal("RiskAdminNoChanges", draft.Batch("Uma justificativa longa o bastante").ReasonKey);

        draft.Set(10, new RiskContextValues(5, null, null));
        draft.Set(20, new RiskContextValues(4, true, 51));
        Assert.Equal("RiskContextJustificationTooShort",
            draft.Batch(new string('x', EntityRiskContextRules.MinimumJustificationLength - 1)).ReasonKey);
        Assert.Equal("RiskContextJustificationTooLong",
            draft.Batch(new string('x', EntityRiskContextRules.MaximumJustificationLength + 1)).ReasonKey);

        var (request, reason) = draft.Batch("  " + new string('x', EntityRiskContextRules.MinimumJustificationLength) + "  ");
        Assert.Null(reason);
        Assert.Equal(new string('x', EntityRiskContextRules.MinimumJustificationLength), request!.Justification);
        Assert.Equal([10, 20], request.Changes.Select(c => c.EntityId));
    }

    [Fact]
    public void TestDiscardForgetsEveryEditAndTheChangesListBeforeAndAfter()
    {
        var draft = Loaded();
        draft.Set(10, new RiskContextValues(5, null, null));
        var change = Assert.Single(draft.Changes());
        Assert.Equal(3, change.Before.Criticality);
        Assert.Equal(5, change.After.Criticality);

        draft.Discard();
        Assert.False(draft.IsDirty);
        Assert.Equal(3, draft.Current(10).Criticality);
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.Set(99, default));
    }
}
