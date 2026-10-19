using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GUIClient.Models;
using GUIClient.Navigation;
using GUIClient.Tests.Tools.RiskIndex;
using GUIClient.Tools.Navigation;
using JetBrains.Annotations;
using Model.Authentication;
using Xunit;

namespace GUIClient.Tests.Tools.Navigation;

/// <summary>
/// T255 (S40 §5.9.2, §10.3): opening a record in another module — refused for a module the user may not open (defence in
/// depth behind the disabled button) and for Incidents behind face verification; found among the loaded records, else
/// among those the service lists again, else "not in your scope"; the Vulnerabilities sub-views' filters.
/// </summary>
[TestSubject(typeof(RecordNavigation))]
public class RecordNavigationTest
{
    private sealed record Row(int Id);

    private static AuthenticatedUserInfo User(params string[] permissions) =>
        new() { UserName = "u", UserRole = "user", UserPermissions = new List<string>(permissions) };

    [Fact]
    public void TestAModuleTheUserMayOpenIsOpened()
    {
        Assert.Equal(RecordNavigationDecision.Open,
            RecordNavigation.Decide(new NavigationTarget(AvaliableViews.Risk, 1), User("riskmanagement"), faceIdRequired: true));
        Assert.Equal(RecordNavigationDecision.Open,
            RecordNavigation.Decide(new NavigationTarget(AvaliableViews.Incidents, 1), User("incident_management"), false));
    }

    [Fact]
    public void TestAModuleTheUserMayNotOpenIsRefused()
    {
        Assert.Equal(RecordNavigationDecision.NotAllowed,
            RecordNavigation.Decide(new NavigationTarget(AvaliableViews.Risk, 1), User("hosts"), false));
        Assert.Equal(RecordNavigationDecision.NotAllowed,
            RecordNavigation.Decide(new NavigationTarget(AvaliableViews.Vulnerabilities, 1, NavigationTarget.HostSubView),
                User("hosts"), false));
        Assert.Equal(RecordNavigationDecision.NotAllowed,
            RecordNavigation.Decide(new NavigationTarget(AvaliableViews.Entities, 1), null, false));
    }

    /// <summary>Only the navigation bar runs face verification, so a link must not open Incidents around it.</summary>
    [Fact]
    public void TestIncidentsBehindFaceVerificationAreRefused()
    {
        Assert.Equal(RecordNavigationDecision.RequiresFaceVerification,
            RecordNavigation.Decide(new NavigationTarget(AvaliableViews.Incidents, 7), User("incident_management"), true));
        // Permission is checked first: no permission is "not allowed", whatever the verification.
        Assert.Equal(RecordNavigationDecision.NotAllowed,
            RecordNavigation.Decide(new NavigationTarget(AvaliableViews.Incidents, 7), User(), true));
    }

    [Fact]
    public async Task TestALoadedRecordIsSelectedWithoutAskingTheServiceAgain()
    {
        var reloads = 0;
        var found = await RecordNavigation.FindAsync<Row>([new Row(1), new Row(2)], r => r.Id == 2, () =>
        {
            reloads++;
            return Task.FromResult<IEnumerable<Row>?>([]);
        });

        Assert.Equal(new Row(2), found);
        Assert.Equal(0, reloads);
    }

    [Fact]
    public async Task TestARecordNotLoadedIsLookedForInTheServicesList()
    {
        var found = await RecordNavigation.FindAsync<Row>(null, r => r.Id == 3,
            () => Task.FromResult<IEnumerable<Row>?>([new Row(3)]));

        Assert.Equal(new Row(3), found);
    }

    [Fact]
    public async Task TestARecordOutOfScopeOrMissingIsNotFound()
    {
        Assert.Null(await RecordNavigation.FindAsync<Row>([new Row(1)], r => r.Id == 9,
            () => Task.FromResult<IEnumerable<Row>?>([new Row(1)])));
        Assert.Null(await RecordNavigation.FindAsync<Row>([], r => r.Id == 9, () => Task.FromResult<IEnumerable<Row>?>(null)));
        // A refusal or a transport failure is "not shown", never an exception in the navigation — and it is reported.
        Exception? reported = null;
        Assert.Null(await RecordNavigation.FindAsync<Row>([], r => r.Id == 9,
            () => Task.FromException<IEnumerable<Row>?>(new InvalidOperationException("403")), ex => reported = ex));
        Assert.Equal("403", reported?.Message);
    }

    /// <summary>
    /// A module opened for the first time by a link starts its first load as it is built: the record is looked for in what
    /// that load brings, not in a second read the first one would then overwrite (dropping the selection).
    /// </summary>
    [Fact]
    public async Task TestARecordIsLookedForOnlyOnceTheFirstLoadHasEnded()
    {
        var firstLoad = new TaskCompletionSource();
        IEnumerable<Row>? loaded = null;
        var reloads = 0;

        var finding = RecordNavigation.FindAfterLoadAsync(firstLoad.Task, () => loaded, r => r.Id == 4, () =>
        {
            reloads++;
            return Task.FromResult<IEnumerable<Row>?>([new Row(4)]);
        });
        await Task.Yield();
        Assert.False(finding.IsCompleted);
        loaded = [new Row(3), new Row(4)];
        firstLoad.SetResult();

        Assert.Same(loaded.Last(), await finding);
        Assert.Equal(0, reloads);
    }

    [Fact]
    public async Task TestAFailedFirstLoadIsReportedAndTheRecordLookedForAnew()
    {
        Exception? reported = null;

        var found = await RecordNavigation.FindAfterLoadAsync<Row>(Task.FromException(new InvalidOperationException("load")),
            () => null, r => r.Id == 5, () => Task.FromResult<IEnumerable<Row>?>([new Row(5)]), ex => reported = ex);
        await RecordNavigation.AwaitLoadAsync(null);

        Assert.Equal(new Row(5), found);
        Assert.Equal("load", reported?.Message);
    }

    [Fact]
    public void TestTheVulnerabilitiesSubViewsFilterByHostOrApplication()
    {
        Assert.Equal("host=12", RecordNavigation.FindingsFilter(NavigationTarget.HostSubView, 12));
        Assert.Equal("application=30", RecordNavigation.FindingsFilter(NavigationTarget.EntitySubView, 30));
        Assert.Null(RecordNavigation.FindingsFilter(null, 12));
        Assert.Null(RecordNavigation.FindingsFilter("other", 12));
        Assert.Equal("id==5", RecordNavigation.HostFilter(5));
    }

    [Fact]
    public void TestTheRegistryFindsTheModuleThatRegisteredLast()
    {
        var registry = new SelectableModules();
        var first = new FakeModule();
        var second = new FakeModule();

        Assert.Null(registry.Find(AvaliableViews.Risk));
        registry.Register(AvaliableViews.Risk, first);
        registry.Register(AvaliableViews.Devices, second);
        Assert.Same(first, registry.Find(AvaliableViews.Risk));
        registry.Register(AvaliableViews.Risk, second);
        Assert.Same(second, registry.Find(AvaliableViews.Risk));
        Assert.Throws<ArgumentNullException>(() => registry.Register(AvaliableViews.Risk, null!));
        GC.KeepAlive(first);
    }

    [Fact]
    public void TestTheToastsResolveInTheThreeResourceFiles()
    {
        string[] keys = [RecordNavigation.NotAllowedKey, RecordNavigation.FaceVerificationKey, RecordNavigation.OutOfScopeKey];
        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            Assert.All(keys, k => Assert.True(texts.ContainsKey(k), $"{file} lacks {k}"));
        }

        Assert.Equal("O registro não está no seu escopo.", RiskIndexTestText.Portuguese(RecordNavigation.OutOfScopeKey));
        Assert.Contains("{0}", RiskIndexTestText.Portuguese(RecordNavigation.NotAllowedKey));
    }

    private sealed class FakeModule : ISelectableModule
    {
        public Task<bool> SelectRecordAsync(int id, string? subView) => Task.FromResult(true);
    }
}
