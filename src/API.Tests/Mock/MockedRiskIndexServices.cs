using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using Model.RiskIndex;
using NSubstitute;
using ServerServices.RiskIndex.Profiles;
using ServerServices.RiskIndex.Read;

namespace API.Tests.Mock;

/// <summary>
/// Deterministic doubles of the index's read service and profile lifecycle (T249, T250) for the controller tests. The node
/// key — or the profile id — chooses the refusal, so every branch of the error mapping is reachable without a per-test
/// double: <c>bad</c>/400, <c>shadow</c>/403, <c>gone</c>/404, <c>partial</c>/409, <c>busy</c>/503 with Retry-After.
/// </summary>
public static class MockedRiskIndexReadService
{
    public static readonly DateOnly Date = new(2026, 10, 5);

    private static readonly RiskIndexNodeRef Org = new("org", "Organization", "organization");

    public static RiskIndexProfileSummaryDto Profile => new(1, 1, "Pilot", "balanced", RiskIndexProfileStatus.Active,
        new DateTime(2026, 9, 1, 5, 0, 0, DateTimeKind.Utc), null, "author", "approver", false, "CRTI");

    internal static void Gate(string? node)
    {
        switch (node)
        {
            case "bad": throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidNode, "bad key");
            case "shadow": throw RiskIndexException.Forbidden(RiskIndexErrors.InShadow, "shadow");
            case "gone": throw RiskIndexException.NotFound(RiskIndexErrors.NodeNotFound, "gone");
            case "partial": throw RiskIndexException.Conflict(RiskIndexErrors.TrendUnavailablePartial, "partial");
            case "busy": throw RiskIndexException.Unavailable(RiskIndexErrors.DerivedBusy, "busy", TimeSpan.FromSeconds(5));
            case "boom": throw new InvalidOperationException("boom");
        }
    }

    public static IRiskIndexReadService Create()
    {
        var service = Substitute.For<IRiskIndexReadService>();
        var overview = new RiskIndexOverviewDto(Org, [], Date, Date.ToDateTime(TimeOnly.MinValue), RiskIndexProvenance.Official,
            Profile, RiskIndexMode.Interim,
            new RiskIndexHeadlineDto(64, 63.69, 63.69, "b1", 1, RiskIndexDisplayState.Point, 63.7, 65.4, false, null, [], true, null),
            [new RiskIndexCategoryDto(RiskIndexCategory.Reg, RiskIndexCategoryState.Available, 67.7, 68, "b1", 0.3, 0.3, 20.3, null,
                0.83, 1, 0, null, null, [])], [], [], new RiskIndexQualitySummaryDto(0.92, 0.99, 0.91, "high", []), 12, [], []);

        service.GetLatestAsync(Arg.Any<CancellationToken>())
            .Returns(new RiskIndexLatestDto(Date, 1, RiskIndexMode.Interim, RiskIndexPublicationState.Shadow));
        service.GetSnapshotDatesAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DateOnly> { Date });
        service.GetPublicationAsync(Arg.Any<CancellationToken>())
            .Returns(new RiskIndexPublicationDto(RiskIndexPublicationState.Shadow, null, null));
        service.SetPublicationAsync(Arg.Any<RiskIndexPublicationState>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<RiskIndexPublicationState>(0) == RiskIndexPublicationState.Published
                ? throw RiskIndexException.Conflict(RiskIndexErrors.NoApprovedProfile, "none")
                : new RiskIndexPublicationDto(RiskIndexPublicationState.Shadow, null, null));

        service.GetOverviewAsync(Arg.Any<string?>(), Arg.Any<DateOnly?>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<string?>(0)); return overview; });
        service.GetCategoriesAsync(Arg.Any<string?>(), Arg.Any<DateOnly?>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<string?>(0)); return (IReadOnlyList<RiskIndexCategoryDto>)overview.Categories; });
        service.GetTrendAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<RiskIndexCategory>(), Arg.Any<DateOnly?>(),
                Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<string?>(0));
                return new RiskIndexTrendDto(Org, call.ArgAt<int>(1), call.ArgAt<RiskIndexCategory>(2), [], [], [], [], [], [], null, null);
            });
        service.GetNodesAsync(Arg.Any<string?>(), Arg.Any<RiskIndexDimension>(), Arg.Any<string?>(), Arg.Any<DateOnly?>(),
                Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<string?>(0));
                return new RiskIndexNodeChildrenDto(Org, call.ArgAt<RiskIndexDimension>(1), [], [], 0);
            });
        service.GetObjectsAsync(Arg.Any<string?>(), Arg.Any<DateOnly?>(), Arg.Any<RiskIndexObjectQuery>(), Arg.Any<RiskIndexCaller>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<string?>(0));
                var row = new RiskIndexObjectRowDto(new RiskIndexObjectLabelDto(new RiskIndexObjectRef(RiskIndexObjectKind.Scenario, "1"),
                        "Scenario #1", "riskmanagement"), null, RiskIndexCategory.Reg, 0.84, 5, "impact", RiskIndexObjectState.Assessed, 1,
                    null, 20.3, false, 0, ["REG-RES"], null);
                return ((IReadOnlyList<RiskIndexObjectRowDto>)new List<RiskIndexObjectRowDto> { row }, 37);
            });
        service.GetObjectAsync(Arg.Any<RiskIndexObjectKind>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateOnly?>(),
                Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.ArgAt<string>(1) == "404") throw RiskIndexException.NotFound(RiskIndexErrors.ObjectNotFound, "gone");
                return new RiskIndexObjectDetailDto(new RiskIndexObjectLabelDto(
                        new RiskIndexObjectRef(call.ArgAt<RiskIndexObjectKind>(0), call.ArgAt<string>(1)), "host-1", null), "Server", Date,
                    new RiskIndexCriticalityDto(null, null, 0, 4, "manual", null, null, false), null, [], null, null, [], [], []);
            });
        service.GetInventoryAsync(Arg.Any<string?>(), Arg.Any<RiskIndexTabGroup?>(), Arg.Any<DateOnly?>(), Arg.Any<RiskIndexCaller>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<string?>(0));
                if (call.ArgAt<RiskIndexTabGroup?>(1) is not { } tab) throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidTab, "tab");
                return new RiskIndexInventoryDto(tab, 10, "exp_objects", [], new RiskIndexStateCountsDto(6, 2, 2, 0),
                    new Dictionary<string, int> { ["b0"] = 6 });
            });
        service.GetChangesAsync(Arg.Any<string?>(), Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<RiskIndexCaller>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<string?>(0));
                return new RiskIndexChangesDto(Org, Date.AddDays(-1), Date, -3.71, -4, [], [], 0, []);
            });
        service.GetContributionsAsync(Arg.Any<string?>(), Arg.Any<DateOnly?>(), Arg.Any<RiskIndexCategory?>(), Arg.Any<RiskIndexCaller>(),
                Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<string?>(0)); return new RiskIndexContributionsDto(Org, 0.51, []); });
        service.GetGovernanceIndicatorsAsync(Arg.Any<string?>(), Arg.Any<DateOnly?>(), Arg.Any<RiskIndexCaller>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<string?>(0));
                return new RiskIndexGovernanceIndicatorsDto(3, 30, 1, 0.82, 9, 1, 0.71, "requires_m43", "requires_m45_m46");
            });
        return service;
    }
}

/// <summary>The profile lifecycle's double: id 1 succeeds; 400, 403, 404 and 409 refuse with a representative code.</summary>
public static class MockedRiskIndexProfilesService
{
    public static RiskIndexProfileDto Profile(int id = 1, int revision = 0) => new(id, 1, "Pilot", "balanced", RiskIndexMode.Interim,
        RiskIndexProfileStatus.Draft, new RiskIndexParameters(), null, null, null, false, null, "author",
        new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, null, null, null, null, null, revision, false);

    internal static void Gate(int id)
    {
        switch (id)
        {
            case 400: throw RiskIndexException.BadRequest(RiskIndexErrors.CommitteeRefRequired, "ref");
            case 403: throw RiskIndexException.Forbidden(RiskIndexErrors.SodAuthorCannotApprove, "sod");
            case 404: throw RiskIndexException.NotFound(RiskIndexErrors.ProfileNotFound, "gone");
            case 409: throw RiskIndexException.Conflict(RiskIndexErrors.StaleDraft, "stale");
        }
    }

    public static IRiskIndexProfilesService Create()
    {
        var service = Substitute.For<IRiskIndexProfilesService>();
        service.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<RiskIndexProfileSummaryDto> { MockedRiskIndexReadService.Profile });
        service.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call => { Gate(call.ArgAt<int>(0)); return Profile(call.ArgAt<int>(0)); });
        service.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((RiskIndexProfileDto?)null);
        service.DiffAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(1)); return new RiskIndexProfileDiffDto(call.ArgAt<int>(0), call.ArgAt<int>(1), []); });
        service.GetSchema().Returns(RiskIndexProfileRules.Schema());
        service.GetPresets().Returns(RiskIndexPresets.List());
        service.CreateAsync(Arg.Any<RiskIndexProfileCreateRequest>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<RiskIndexProfileCreateRequest>(0).Name == "dup"
                ? throw RiskIndexException.Conflict(RiskIndexErrors.OpenDraftExists, "dup")
                : Profile(7));
        service.UpdateAsync(Arg.Any<int>(), Arg.Any<RiskIndexProfileUpdateRequest>(), Arg.Any<int>(), Arg.Any<RiskIndexCaller>(),
                Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Profile(call.ArgAt<int>(0), call.ArgAt<int>(2) + 1); });
        service.DeleteAsync(Arg.Any<int>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Task.CompletedTask; });
        service.ValidateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return RiskIndexProfileRules.Validate(RiskIndexMode.Interim, new RiskIndexParameters(), null, null, null); });
        service.RequestPreviewAsync(Arg.Any<int>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return new RiskIndexPreviewStatusDto(5, RiskIndexPreviewState.Queued, 0, "h", null, null); });
        service.GetPreviewAsync(Arg.Any<int>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<int>(0));
                return new RiskIndexPreviewDto(new RiskIndexPreviewStatusDto(5, RiskIndexPreviewState.Completed, 100, "h", null, null),
                    [], 0.97, 34, [], [], 0.51, 0.53, [], [], false);
            });
        service.SubmitAsync(Arg.Any<int>(), Arg.Any<RiskIndexSubmitRequest>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Profile(call.ArgAt<int>(0)) with { Status = RiskIndexProfileStatus.PendingApproval }; });
        service.GetApprovalEligibilityAsync(Arg.Any<int>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<int>(0));
                return new RiskIndexApprovalEligibilityDto(
                    [new RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode.SodAuthorCannotApprove, true, "ok")], "role:102", true, []);
            });
        service.ApproveAsync(Arg.Any<int>(), Arg.Any<RiskIndexApproveRequest>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Profile(call.ArgAt<int>(0)) with { Status = RiskIndexProfileStatus.Active }; });
        service.RejectAsync(Arg.Any<int>(), Arg.Any<RiskIndexRejectRequest>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Profile(call.ArgAt<int>(0)) with { Status = RiskIndexProfileStatus.Rejected }; });
        service.RetireAsync(Arg.Any<int>(), Arg.Any<RiskIndexRetireRequest>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Profile(call.ArgAt<int>(0)) with { Status = RiskIndexProfileStatus.Retired }; });
        service.AttachAsync(Arg.Any<int>(), Arg.Any<RiskIndexAttachRequest>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Task.CompletedTask; });
        service.ListAttachmentsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<int>(0));
                return (IReadOnlyList<Model.DTO.FileListing>)new List<Model.DTO.FileListing>
                    { new() { Name = "ata-crti.pdf", UniqueName = "u1", OwnerId = 20 } };
            });
        service.GetBridgeAsync(Arg.Any<int>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return new RiskIndexBridgeDto(1, 2, MockedRiskIndexReadService.Date, [], false); });
        service.GetHistoryAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<RiskIndexCaller>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return (IReadOnlyList<AuditLog>)new List<AuditLog>(); });
        return service;
    }
}
