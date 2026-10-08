using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Controllers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DecisionCycle;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.9 (S50 §4.7, §8 EV1–EV2) — the third line reads the governance evidence pack in the system rather than
/// receiving it by hand: the trail and the CSV, which store nothing. The PDF is stored as a report through the reporting
/// engine — a GET that writes — so the third line is refused it, and nothing is stored.
/// </summary>
[TestSubject(typeof(AuditTrailController))]
public class ThirdLineEvidenceTest : BaseControllerTest
{
    private static (AuditTrailController Controller, IReportsService Reports) AsAuditor()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Loopback },
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "testUser"), new Claim("Permission", ThirdLineAssurance.PermissionKey)
            }, "Bearer"))
        });

        var reports = Substitute.For<IReportsService>();
        var controller = ResolveController<AuditTrailController>(services =>
        {
            services.AddSingleton(accessor);
            services.AddSingleton(reports);
        });

        return (controller, reports);
    }

    private static readonly DateTime From = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>EV1 — the third line takes the CSV and the trail.</summary>
    [Fact]
    public async Task TestEV1_TheThirdLineReadsTheEvidence()
    {
        var (controller, _) = AsAuditor();

        Assert.IsType<FileContentResult>(await controller.GetEvidenceReport(null, From, To, "csv"));
        Assert.IsType<OkObjectResult>((await controller.GetEvidence(null, From, To)).Result);
    }

    /// <summary>EV2 — the PDF stores a report: refused to the third line with the read-only rule, and nothing is stored.</summary>
    [Fact]
    public async Task TestEV2_ThePdfIsAWriteAndIsRefused()
    {
        var (controller, reports) = AsAuditor();

        var result = Assert.IsType<ObjectResult>(await controller.GetEvidenceReport(null, From, To, "pdf"));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Contains(ThirdLineAssurance.ReadOnlyRule, System.Text.Json.JsonSerializer.Serialize(result.Value));
        await reports.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!);
    }
}
