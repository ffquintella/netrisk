using System.Net;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Model.Governance;
using NSubstitute;
using RiskPortal.Models;
using RiskPortal.Pages.Campaign;
using RiskPortal.Services;
using RiskPortal.Tests.Mock;
using Xunit;

namespace RiskPortal.Tests.Pages;

/// <summary>
/// The reviewer flow: rank, then decide (Track 8 milestone 8.6.4).
///
/// The property worth asserting here is that the page turns a form post into exactly the right
/// request and refuses to send an incomplete one. The server validates as well — and its refusal is
/// what the reviewer sees when this misses something — but a round trip to be told "a justification
/// is required" is a worse experience on a phone than being told immediately, and a page that sends
/// an acceptance with no expiry is a page that would create one if the server ever relaxed.
/// </summary>
[TestSubject(typeof(IndexModel))]
public class CampaignDecisionTest
{
    private readonly FakePortalApiClient _api = new();

    private IndexModel NewPage(IPortalSession? session = null)
    {
        var page = new IndexModel(_api, session ?? new FakePortalSession(), NullLogger<IndexModel>.Instance);

        var httpContext = new DefaultHttpContext();

        page.PageContext = new PageContext
        {
            HttpContext = httpContext,
            RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            ActionDescriptor = new Microsoft.AspNetCore.Mvc.RazorPages.CompiledPageActionDescriptor()
        };

        page.TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>());

        return page;
    }

    private static CampaignDetail DetailWith(params RiskReviewDecision[] decisions)
    {
        var detail = new CampaignDetail
        {
            Campaign = new CampaignSummary
            {
                Id = 1, Name = "Risk review 2026Q3", EntityId = 1,
                DueDate = DateTime.UtcNow.AddDays(20), TotalItems = decisions.Length
            }
        };

        for (var i = 0; i < decisions.Length; i++)
            detail.Items.Add(new ReviewItem
            {
                ItemId = i + 1, RiskId = i + 1, Subject = $"Risk {i + 1}",
                Inherent = 8f, Residual = 4f, Decision = decisions[i]
            });

        detail.Campaign.DecidedItems = decisions.Count(d => d != RiskReviewDecision.Pending);

        return detail;
    }

    // --- reading the screen -----------------------------------------------------------------

    [Fact]
    public async Task AnUnauthenticatedVisitorIsSentToSignIn()
    {
        var page = NewPage(new FakePortalSession(token: null));

        var result = await page.OnGetAsync(1);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/SignIn", redirect.PageName);
    }

    /// <summary>
    /// A campaign the reviewer cannot reach comes back null from the API (403 or 404), and the page
    /// says why in the reviewer's terms rather than showing an empty screen.
    /// </summary>
    [Fact]
    public async Task ACampaignTheReviewerCannotReachRedirectsWithAnExplanation()
    {
        _api.Detail = null;

        var page = NewPage();
        var result = await page.OnGetAsync(1);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Index", redirect.PageName);
        Assert.Contains("not appointed", page.TempData["Error"]!.ToString()!);
    }

    [Fact]
    public async Task AReachableCampaignRenders()
    {
        _api.Detail = DetailWith(RiskReviewDecision.Pending, RiskReviewDecision.Accepted);

        var page = NewPage();

        Assert.IsType<PageResult>(await page.OnGetAsync(1));
        Assert.Equal(2, page.Detail!.Items.Count);
        Assert.False(page.Detail.IsComplete);
    }

    [Fact]
    public async Task ACampaignWithEveryItemDecidedIsComplete()
    {
        _api.Detail = DetailWith(RiskReviewDecision.Accepted, RiskReviewDecision.Escalated);

        var page = NewPage();
        await page.OnGetAsync(1);

        Assert.True(page.Detail!.IsComplete);
    }

    // --- ranking ---------------------------------------------------------------------------

    [Fact]
    public async Task RankingSendsTheSubmittedOrder()
    {
        var page = NewPage();

        await page.OnPostRankAsync(1, "3,1,2");

        var (campaignId, order) = Assert.Single(_api.Rankings);
        Assert.Equal(1, campaignId);
        Assert.Equal([3, 1, 2], order);
    }

    [Fact]
    public async Task AnEmptyOrderIsRefusedWithoutCallingTheApi()
    {
        var page = NewPage();

        await page.OnPostRankAsync(1, "   ");

        Assert.Empty(_api.Rankings);
        Assert.Contains("No ordering", page.TempData["Error"]!.ToString()!);
    }

    [Fact]
    public async Task ARefusedRankingShowsTheServersMessage()
    {
        _api.NextWriteResult = PortalResult.Fail("These item ids are not in campaign 1: 999.");

        var page = NewPage();
        await page.OnPostRankAsync(1, "999");

        Assert.Contains("not in campaign", page.TempData["Error"]!.ToString()!);
    }

    [Theory]
    [InlineData("3,1,2", new[] { 3, 1, 2 })]
    [InlineData(" 3 , 1 ,2 ", new[] { 3, 1, 2 })]
    [InlineData("3,,1", new[] { 3, 1 })]
    [InlineData("3,x,1", new[] { 3, 1 })]
    [InlineData("3,3,1", new[] { 3, 1 })]
    [InlineData("0,-1,2", new[] { 2 })]
    [InlineData("", new int[0])]
    public void TheOrderFieldIsParsedDefensively(string order, int[] expected)
    {
        Assert.Equal(expected, IndexModel.ParseOrder(order));
    }

    // --- accepting -------------------------------------------------------------------------

    [Fact]
    public async Task AcceptingSendsTheJustificationAndAnEndOfDayExpiry()
    {
        var page = NewPage();

        await page.OnPostDecideAsync(1, 2, "Accepted", "Committee minute 14",
            justification: "Replacement is scheduled for Q4; monitoring is in place until then.",
            expiresAt: new DateTime(2026, 12, 15), taskTitle: null, taskOwnerId: null,
            taskDueDate: null, escalateToUserId: null);

        var (campaignId, itemId, request) = Assert.Single(_api.Decisions);

        Assert.Equal(1, campaignId);
        Assert.Equal(2, itemId);
        Assert.Equal(RiskReviewDecision.Accepted, request.Decision);
        Assert.Contains("Replacement is scheduled", request.Acceptance!.BusinessJustification);

        // End of the chosen day, not midnight: a reviewer picking 15 December means "until the end of
        // the 15th", and midnight would expire it a day early.
        Assert.Equal(new DateTime(2026, 12, 15, 23, 59, 59, DateTimeKind.Utc),
            request.Acceptance.ExpiresAt);
    }

    [Fact]
    public async Task AcceptingWithoutAJustificationIsRefusedLocally()
    {
        var page = NewPage();

        await page.OnPostDecideAsync(1, 2, "Accepted", null, justification: "   ",
            expiresAt: DateTime.UtcNow.AddDays(30), taskTitle: null, taskOwnerId: null,
            taskDueDate: null, escalateToUserId: null);

        Assert.Empty(_api.Decisions);
        Assert.Contains("business justification", page.TempData["Error"]!.ToString()!);
    }

    [Fact]
    public async Task AcceptingWithoutAnExpiryIsRefusedLocally()
    {
        var page = NewPage();

        await page.OnPostDecideAsync(1, 2, "Accepted", null, justification: "Because.",
            expiresAt: null, taskTitle: null, taskOwnerId: null, taskDueDate: null,
            escalateToUserId: null);

        Assert.Empty(_api.Decisions);
        Assert.Contains("expiry date", page.TempData["Error"]!.ToString()!);
    }

    [Fact]
    public async Task AcceptingWithAnExpiryInThePastIsRefusedLocally()
    {
        var page = NewPage();

        await page.OnPostDecideAsync(1, 2, "Accepted", null, justification: "Because.",
            expiresAt: DateTime.UtcNow.AddDays(-1), taskTitle: null, taskOwnerId: null,
            taskDueDate: null, escalateToUserId: null);

        Assert.Empty(_api.Decisions);
    }

    /// <summary>
    /// The appetite ceiling is a server decision, and its message is written for a business reviewer.
    /// The page has to show it verbatim and reopen the form on that item, not swallow it.
    /// </summary>
    [Fact]
    public async Task AnAppetiteRefusalIsShownVerbatimAndReopensTheItem()
    {
        _api.NextWriteResult = PortalResult.Fail(
            "Residual 9.10 is above the acceptance ceiling of 6.00. This risk cannot be accepted as " +
            "it stands.");

        var page = NewPage();

        var result = await page.OnPostDecideAsync(1, 2, "Accepted", null,
            justification: "It is fine.", expiresAt: DateTime.UtcNow.AddDays(30), taskTitle: null,
            taskOwnerId: null, taskDueDate: null, escalateToUserId: null);

        Assert.Contains("above the acceptance ceiling", page.TempData["Error"]!.ToString()!);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(2, redirect.RouteValues!["open"]);
    }

    // --- requesting mitigation --------------------------------------------------------------

    [Fact]
    public async Task RequestingMitigationSendsOneTaskWithItsOwnerAndDueDate()
    {
        var page = NewPage();

        await page.OnPostDecideAsync(1, 3, "MitigationRequested", "Please prioritise this",
            justification: null, expiresAt: null, taskTitle: "Rebuild the appliance",
            taskOwnerId: 42, taskDueDate: new DateTime(2026, 11, 30), escalateToUserId: null);

        var (_, itemId, request) = Assert.Single(_api.Decisions);

        Assert.Equal(3, itemId);
        var task = Assert.Single(request.Tasks!);
        Assert.Equal("Rebuild the appliance", task.Title);
        Assert.Equal(42, task.OwnerId);
        Assert.Equal(new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc), task.DueDate);
    }

    [Fact]
    public async Task RequestingMitigationWithNoTaskTitleIsRefusedLocally()
    {
        var page = NewPage();

        await page.OnPostDecideAsync(1, 3, "MitigationRequested", null, null, null,
            taskTitle: "  ", taskOwnerId: 42, taskDueDate: null, escalateToUserId: null);

        Assert.Empty(_api.Decisions);
        Assert.Contains("not a plan of action", page.TempData["Error"]!.ToString()!);
    }

    // --- escalating -------------------------------------------------------------------------

    [Fact]
    public async Task EscalatingSendsTheNamedApprover()
    {
        var page = NewPage();

        await page.OnPostDecideAsync(1, 4, "Escalated", "Above my delegated authority",
            null, null, null, null, null, escalateToUserId: 7);

        var (_, _, request) = Assert.Single(_api.Decisions);

        Assert.Equal(RiskReviewDecision.Escalated, request.Decision);
        Assert.Equal(7, request.EscalateToUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task EscalatingWithoutARealApproverIsRefusedLocally(int? approver)
    {
        var page = NewPage();

        await page.OnPostDecideAsync(1, 4, "Escalated", null, null, null, null, null, null, approver);

        Assert.Empty(_api.Decisions);
        Assert.Contains("named senior approver", page.TempData["Error"]!.ToString()!);
    }

    // --- the decision itself ----------------------------------------------------------------

    [Theory]
    [InlineData("Pending")]
    [InlineData("")]
    [InlineData("something-else")]
    public async Task AnUnusableDecisionIsRefusedLocally(string decision)
    {
        var page = NewPage();

        await page.OnPostDecideAsync(1, 2, decision, null, null, null, null, null, null, null);

        Assert.Empty(_api.Decisions);
    }

    [Fact]
    public async Task ASuccessfulDecisionSaysWhatWasRecorded()
    {
        var page = NewPage();

        await page.OnPostDecideAsync(1, 2, "Escalated", null, null, null, null, null, null, 7);

        Assert.Contains("Escalated", page.TempData["Notice"]!.ToString()!);
        Assert.Null(page.TempData["Error"]);
    }

    [Fact]
    public async Task DecidingWithoutASessionIsSentToSignIn()
    {
        var page = NewPage(new FakePortalSession(token: null));

        var result = await page.OnPostDecideAsync(1, 2, "Escalated", null, null, null, null, null,
            null, 7);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Empty(_api.Decisions);
    }
}
