using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using GUIClient.Tools;
using GUIClient.UiTests.Infrastructure;
using GUIClient.Views;
using Material.Icons;
using Material.Icons.Avalonia;
using Model.Risks.Chain;
using NSubstitute;
using Xunit;
using static GUIClient.UiTests.Flows.Risks.RiskScreen;
using static GUIClient.UiTests.Flows.Risks.RiskServer;

namespace GUIClient.UiTests.Flows.Risks;

/// <summary>
/// Stage 9.1: the linkage-chain block of the risk detail opens <see cref="EditRiskChainDialog"/>, where every add and
/// remove is saved at once; closing it after a change re-reads the chain into the detail.
/// </summary>
public class RiskChainFlowTest
{
    private const int ProcessEntityId = 70;
    private const string ProcessName = "Folha de pagamento";

    [UiFact]
    public async Task TestAddingAndRemovingLinksSavesEachAndRefreshesTheDetail()
    {
        Arrange();
        var chain = new ChainServer();

        var screen = await OpenAsync(RiskUsers.Reader);
        await screen.SelectAsync(LeakSubject);
        await UiHost.WaitUntilAsync(() => screen.DetailValue(Localized("ChainLevelItService") + ":") == "CRM",
            "the chain block to show the risk's IT service");

        UiInput.ScrollToAndClick(EditChainButton(screen));
        var dialog = await screen.WaitForDialogAsync<EditRiskChainDialog>();
        await UiHost.WaitUntilAsync(() => LinkRows(dialog).Count == 2, "the risk's two links to be listed");

        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == LeakSubject);
        Assert.Contains(Localized("ChainOriginDeclared"), CellTexts(dialog));
        Assert.Contains(Localized("ChainOriginLegacy"), CellTexts(dialog));

        // A Legacy link comes from the risk's Entity field: it cannot be removed here, and the dialog says where.
        var remove = dialog.Find<Button>(b => b.Classes.Contains("operation"));
        UiInput.Click(Row(dialog, "Diretoria Financeira"));
        Assert.False(remove.IsEffectivelyEnabled);
        Assert.True(dialog.Find<TextBlock>(t => t.Text == Localized("ChainLegacyLinkHint")).IsEffectivelyVisible);

        // A Declared one can.
        UiInput.Click(Row(dialog, "CRM"));
        Assert.True(remove.IsEffectivelyEnabled);
        UiInput.Click(remove);
        await UiHost.WaitUntilAsync(() => LinkRows(dialog).Count == 1, "the removed link to leave the grid");
        await chain.Service.Received(1).DeleteLinkAsync(Leak, ChainServer.CrmLinkId);

        // Add: the level first, then a target of that level's entity types.
        var add = dialog.Find<Button>(b => b.GetVisualDescendants().OfType<MaterialIcon>().Any(i => i.Kind == MaterialIconKind.Plus));
        Assert.False(add.IsEffectivelyEnabled);
        var combos = dialog.GetVisualDescendants().OfType<ComboBox>().ToList();
        await UiInput.ChooseAsync(combos[0], Localized("ChainLevelProcess"));
        await UiInput.ChooseAsync(combos[1], $"{ProcessName} ({Localized("businessProcess")})");
        Assert.True(add.IsEffectivelyEnabled);
        UiInput.Click(add);

        await UiHost.WaitUntilAsync(() => LinkRows(dialog).Count == 2, "the added link to join the grid");
        await chain.Service.Received(1).AddLinkAsync(Leak,
            Arg.Is<RiskChainLinkCreateDto>(r => r.EntityId == ProcessEntityId && r.HostId == null));
        Assert.Contains(ProcessName, CellTexts(dialog));

        // Close reports the change, and the detail re-reads the chain.
        UiInput.Click(dialog.Find<Button>(b => b.Classes.Contains("dialog2")));
        await screen.WaitForDialogsToCloseAsync("the chain editor to close");
        await UiHost.WaitUntilAsync(() => screen.DetailValue(Localized("ChainLevelProcess") + ":") == ProcessName,
            "the detail to show the new process link");
        Assert.Equal(Localized("ChainNotInformed"), screen.DetailValue(Localized("ChainLevelItService") + ":"));

        UiHost.AssertClean();
    }

    /// <summary>
    /// At the Asset level hosts are found by search, which needs <c>hosts</c>: without it the search is off and the
    /// reason is shown; with it a host can be found and linked.
    /// </summary>
    [UiTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestHostSearchAtTheAssetLevelFollowsTheHostsPermission(bool canReadHosts)
    {
        Arrange();
        var chain = new ChainServer();
        UiServices.Current.Double<IHostsService>().GetFilteredAsync(25, 1, Arg.Any<string?>()).Returns(_ =>
            Task.FromResult(new List<Host> { new() { Id = 80, HostName = "srv-crm-01", Ip = "10.0.0.21", Status = 1, Source = "manual" } }));

        var screen = await OpenAsync(canReadHosts ? RiskUsers.With("hosts") : RiskUsers.Reader);
        await screen.SelectAsync(LeakSubject);
        UiInput.ScrollToAndClick(EditChainButton(screen));
        var dialog = await screen.WaitForDialogAsync<EditRiskChainDialog>();
        await UiHost.WaitUntilAsync(() => LinkRows(dialog).Count == 2, "the risk's links to be listed");

        await UiInput.ChooseAsync(dialog.GetVisualDescendants().OfType<ComboBox>().First(), Localized("ChainLevelAsset"));

        var search = dialog.Find<Button>(b => UiInput.TextOf(b) == Localized("Search"));
        var hint = dialog.Find<TextBlock>(t => t.Text == Localized("RequiresHostsPermission"));
        Assert.Equal(canReadHosts, search.IsEffectivelyEnabled);
        Assert.Equal(!canReadHosts, hint.IsEffectivelyVisible);

        if (canReadHosts)
        {
            UiInput.Click(dialog.Find<TextBox>(t => t.IsEffectivelyVisible));
            UiInput.Type(dialog, "srv-crm");
            UiInput.Click(search);
            var targets = dialog.GetVisualDescendants().OfType<ComboBox>().ElementAt(1);
            await UiHost.WaitUntilAsync(() => targets.ItemCount == 1, "the host search to offer the host");
            await UiInput.ChooseAsync(targets, "srv-crm-01 [10.0.0.21]");
            UiInput.Click(dialog.Find<Button>(b =>
                b.GetVisualDescendants().OfType<MaterialIcon>().Any(i => i.Kind == MaterialIconKind.Plus)));

            await chain.Service.Received(1).AddLinkAsync(Leak,
                Arg.Is<RiskChainLinkCreateDto>(r => r.HostId == 80 && r.EntityId == null));
        }

        UiInput.Click(dialog.Find<Button>(b => b.Classes.Contains("dialog2")));
        await screen.WaitForDialogsToCloseAsync("the chain editor to close");

        UiHost.AssertClean();
    }

    /// <summary>Closing without a change does not re-read the chain.</summary>
    [UiFact]
    public async Task TestClosingWithoutAChangeDoesNotRereadTheChain()
    {
        Arrange();
        var chain = new ChainServer();
        var screen = await OpenAsync(RiskUsers.Reader);
        await screen.SelectAsync(LeakSubject);
        await chain.Service.Received(1).GetRiskChainAsync(Leak);

        UiInput.ScrollToAndClick(EditChainButton(screen));
        var dialog = await screen.WaitForDialogAsync<EditRiskChainDialog>();
        await UiHost.WaitUntilAsync(() => LinkRows(dialog).Count == 2, "the risk's links to be listed");
        UiInput.Click(dialog.Find<Button>(b => b.Classes.Contains("dialog2")));
        await screen.WaitForDialogsToCloseAsync("the chain editor to close");

        // Once for the detail, once for the dialog — and not again.
        await chain.Service.Received(2).GetRiskChainAsync(Leak);

        UiHost.AssertClean();
    }

    private static Button EditChainButton(RiskScreen screen) =>
        screen.Detail.Find<Button>(b => UiInput.TextOf(b) == Localized("EditChain"));

    private static List<DataGridRow> LinkRows(Window dialog) =>
        dialog.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsVisible && r.DataContext is RiskChainLinkRow)
            .ToList();

    private static DataGridRow Row(Window dialog, string target) =>
        LinkRows(dialog).Single(r => ((RiskChainLinkRow)r.DataContext!).TargetName == target);

    private static List<string> CellTexts(Window dialog) =>
        LinkRows(dialog).SelectMany(r => r.GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text ?? "").ToList();

    /// <summary>The chain endpoints for the "leak" risk: a Declared IT service and a Legacy unit, and the entities to pick.</summary>
    private sealed class ChainServer
    {
        public const int CrmLinkId = 901;
        private const int LegacyLinkId = 902;
        private int _nextId = 950;

        private readonly List<RiskChainLinkDto> _links =
        [
            new()
            {
                Id = CrmLinkId, RiskId = Leak, Level = RiskChainLevel.ItService, EntityId = 71, TargetName = "CRM",
                TargetType = "itService", Origin = RiskChainLinkOrigin.Declared, CreatedAt = new DateTime(2026, 9, 1)
            },
            new()
            {
                Id = LegacyLinkId, RiskId = Leak, Level = RiskChainLevel.Objective, EntityId = 60,
                TargetName = "Diretoria Financeira", TargetType = "unit", Origin = RiskChainLinkOrigin.Legacy,
                CreatedAt = new DateTime(2026, 8, 1)
            }
        ];

        public IRiskChainService Service { get; } = UiServices.Current.Double<IRiskChainService>();

        public ChainServer()
        {
            Service.GetRiskChainAsync(Leak).Returns(_ => Task.FromResult(Project()));
            Service.AddLinkAsync(Leak, Arg.Any<RiskChainLinkCreateDto>()).Returns(call =>
            {
                var request = call.Arg<RiskChainLinkCreateDto>();
                var link = new RiskChainLinkDto
                {
                    Id = ++_nextId, RiskId = Leak, EntityId = request.EntityId, HostId = request.HostId,
                    Level = request.HostId is null ? RiskChainLevel.Process : RiskChainLevel.Asset,
                    TargetName = request.HostId is null ? ProcessName : "srv-crm-01",
                    TargetType = request.HostId is null ? "businessProcess" : "host",
                    Origin = RiskChainLinkOrigin.Declared, CreatedAt = new DateTime(2026, 10, 1)
                };
                _links.Add(link);
                return Task.FromResult(link);
            });
            Service.DeleteLinkAsync(Leak, Arg.Any<int>()).Returns(call =>
            {
                _links.RemoveAll(l => l.Id == call.ArgAt<int>(1));
                return Task.FromResult<RiskChainLinkDto?>(null);
            });

            UiServices.Current.Double<IEntitiesService>().GetAllAsync(null, true).Returns(_ => Task.FromResult(new List<Entity>
            {
                new()
                {
                    Id = ProcessEntityId, DefinitionName = "businessProcess", DefinitionVersion = "1", Status = "active",
                    EntitiesProperties = [new EntitiesProperty { Type = "name", Name = "name", Value = ProcessName, OldValue = "" }]
                }
            }));
        }

        private RiskChainDto Project() => new()
        {
            RiskId = Leak,
            Levels = Enum.GetValues<RiskChainLevel>().Select(level => new RiskChainLevelDto
            {
                Level = level, Links = _links.Where(l => l.Level == level).ToList()
            }).ToList()
        };
    }
}
