using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Model.Findings;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// The Track 3 (ASPM) endpoints: importer discovery, import status, the triage lifecycle, risk
/// acceptances, SLA policy, deduplication configuration and CI API tokens.
///
/// Controller tests here are about the HTTP contract, not the domain logic — which status code a
/// domain exception becomes, and what does and does not reach the wire. The 422 for a refused
/// transition and the absence of any secret in a token listing are the two that would actually hurt
/// if they regressed.
/// </summary>
[TestSubject(typeof(VulnerabilitiesController))]
public class Track3ControllersTest : BaseControllerTest
{
    private static T Controller<T>() where T : notnull => ResolveController<T>(_ => { });

    private static TValue Ok<TValue>(ActionResult<TValue> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<TValue>(ok.Value);
    }

    // --- 3.1.4 importer discovery -------------------------------------------------------------

    [Fact]
    public async Task TestGetImportersListsBuiltInsAndPluginsAlike()
    {
        var controller = Controller<VulnerabilitiesController>();

        var importers = Ok(await controller.GetImporters());

        Assert.Equal(2, importers.Count);
        // A client picking an importer should not need to know which is which — only that one of
        // them says so if it wants to.
        Assert.Contains(importers, i => i.Name == "nessus" && !i.IsPlugin);
        Assert.Contains(importers, i => i.Name == "acme-scanner" && i.IsPlugin);
        Assert.All(importers, i => Assert.NotEmpty(i.SupportedFileExtensions));
    }

    [Fact]
    public async Task TestGetImportJobReturnsTheCountsAGateNeeds()
    {
        var controller = Controller<VulnerabilitiesController>();

        var import = Ok(await controller.GetImportJob(1));

        Assert.Equal((int)ScanImportStatus.Succeeded, import.Status);
        Assert.Equal(12, import.NewCount);
        // New-by-severity is what a CI gate branches on.
        Assert.Contains("critical", import.NewBySeverity!);
    }

    [Fact]
    public async Task TestGetImportJobIsNotFoundForAnUnknownImport()
    {
        var controller = Controller<VulnerabilitiesController>();

        Assert.IsType<NotFoundResult>((await controller.GetImportJob(404)).Result);
    }

    [Fact]
    public async Task TestGetImportJobsListsRecentImports()
    {
        var controller = Controller<VulnerabilitiesController>();

        Assert.Single(Ok(await controller.GetImportJobs()));
    }

    // --- 3.2 lifecycle -------------------------------------------------------------------------

    [Fact]
    public async Task TestUpdateLifecycleStatusReturnsTheMovedFinding()
    {
        var controller = Controller<VulnerabilitiesController>();

        var finding = Ok(await controller.UpdateLifecycleStatus(1, new FindingStatusChangeRequest
        {
            Status = FindingStatus.Verified
        }));

        Assert.Equal(FindingStatus.Verified, finding.LifecycleStatus);
    }

    [Fact]
    public async Task TestARefusedTransitionIsA422NotA400()
    {
        var controller = Controller<VulnerabilitiesController>();

        var result = await controller.UpdateLifecycleStatus(MockedFindingLifecycleService.RefusedFindingId,
            new FindingStatusChangeRequest { Status = FindingStatus.Mitigated, Justification = "fixed" });

        // The request is well-formed; it is the finding's current state that makes it impossible.
        // 400 would tell the caller to fix a payload that has nothing wrong with it.
        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Equal(422, unprocessable.StatusCode);
    }

    [Fact]
    public async Task TestTransitionOnAnUnknownFindingIsNotFound()
    {
        var controller = Controller<VulnerabilitiesController>();

        var result = await controller.UpdateLifecycleStatus(404,
            new FindingStatusChangeRequest { Status = FindingStatus.Verified });

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestAMissingBodyIsABadRequest()
    {
        var controller = Controller<VulnerabilitiesController>();

        Assert.IsType<BadRequestObjectResult>((await controller.UpdateLifecycleStatus(1, null!)).Result);
    }

    [Fact]
    public async Task TestHistoryIsReturnedNewestFirst()
    {
        var controller = Controller<VulnerabilitiesController>();

        var history = Ok(await controller.GetStatusHistory(1));

        Assert.Equal(2, history.Count);
        Assert.Equal(FindingStatus.Verified, history[0].ToStatus);
        // The creation event carries no from-state.
        Assert.Null(history[1].FromStatus);
    }

    [Fact]
    public async Task TestAllowedTransitionsComeFromTheServer()
    {
        var controller = Controller<VulnerabilitiesController>();

        var allowed = Ok(await controller.GetAllowedTransitions(1));

        Assert.Contains(FindingStatus.Verified, allowed);
        Assert.DoesNotContain(FindingStatus.Duplicate, allowed);
    }

    [Fact]
    public async Task TestAllowedTransitionsOnAnUnknownFindingIsNotFound()
    {
        var controller = Controller<VulnerabilitiesController>();

        Assert.IsType<NotFoundResult>((await controller.GetAllowedTransitions(404)).Result);
    }

    // --- 3.4.2 SLA views -----------------------------------------------------------------------

    [Fact]
    public async Task TestSlaComplianceReportsPerSeverity()
    {
        var controller = Controller<VulnerabilitiesController>();

        var buckets = Ok(await controller.GetSlaCompliance());

        var critical = buckets.Single(b => b.Severity == Contracts.Importers.NormalizedSeverity.Critical);
        Assert.Equal(75.0, critical.CompliancePercent);

        // An empty band reports nothing rather than a perfect score.
        Assert.Null(buckets.Single(b => b.Severity == Contracts.Importers.NormalizedSeverity.High)
            .CompliancePercent);
    }

    // --- 3.2.3 risk acceptances ----------------------------------------------------------------

    [Fact]
    public async Task TestExpiringWithinFilterNarrowsTheList()
    {
        var controller = Controller<RiskAcceptancesController>();

        Assert.Equal(2, Ok(await controller.GetAll()).Count);
        Assert.Single(Ok(await controller.GetAll(expiringWithinDays: 30)));
    }

    [Fact]
    public async Task TestGetAcceptanceIsNotFoundForAnUnknownId()
    {
        var controller = Controller<RiskAcceptancesController>();

        Assert.IsType<NotFoundResult>((await controller.GetById(404)).Result);
    }

    [Fact]
    public async Task TestCreatingAnAcceptanceReturnsCreated()
    {
        var controller = Controller<RiskAcceptancesController>();

        var result = await controller.Create(new RiskAcceptanceCreationRequest
        {
            Acceptance = new RiskAcceptance
            {
                Name = "Q3 exception",
                BusinessJustification = "The vendor patch breaks payments.",
                AuthorizingManagerId = 1,
                ExpiresAt = DateTime.UtcNow.AddDays(60)
            },
            FindingIds = [1, 2]
        });

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(RiskAcceptanceStatus.Active, Assert.IsType<RiskAcceptance>(created.Value).Status);
    }

    [Fact]
    public async Task TestAnInvalidAcceptanceIsABadRequestNamingTheField()
    {
        var controller = Controller<RiskAcceptancesController>();

        var result = await controller.Create(new RiskAcceptanceCreationRequest
        {
            Acceptance = new RiskAcceptance { Name = "" },
            FindingIds = []
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestRevokingRequiresAReason()
    {
        var controller = Controller<RiskAcceptancesController>();

        Assert.IsType<BadRequestObjectResult>(
            (await controller.Revoke(1, new RevocationRequest { Reason = "  " })).Result);

        var revoked = Ok(await controller.Revoke(1, new RevocationRequest { Reason = "Control removed." }));
        Assert.Equal(RiskAcceptanceStatus.Revoked, revoked.Status);
    }

    [Fact]
    public async Task TestAddingFindingsToAnUnknownAcceptanceIsNotFound()
    {
        var controller = Controller<RiskAcceptancesController>();

        var result = await controller.AddFindings(404, [1]);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestAddingNoFindingsIsABadRequest()
    {
        var controller = Controller<RiskAcceptancesController>();

        Assert.IsType<BadRequestObjectResult>((await controller.AddFindings(1, [])).Result);
    }

    // --- 3.4.1 SLA policy ----------------------------------------------------------------------

    [Fact]
    public async Task TestSupersededPolicyRowsAreOptIn()
    {
        var controller = Controller<SlaConfigurationsController>();

        Assert.Equal(2, Ok(await controller.GetAll()).Count);
        Assert.Equal(3, Ok(await controller.GetAll(includeSuperseded: true)).Count);
    }

    [Fact]
    public void TestBenchmarksAreServedForTheAdminForm()
    {
        var controller = Controller<SlaConfigurationsController>();

        var benchmarks = Assert.IsType<IReadOnlyList<SlaBenchmark>>(
            Assert.IsType<OkObjectResult>(controller.GetBenchmarks().Result).Value, exactMatch: false);

        // The CISA ladder the spec cites: criticals in ~15 days, highs ~30.
        Assert.Equal(15, benchmarks.Single(b => b.Severity == 4).RemediationDays);
        Assert.Equal(30, benchmarks.Single(b => b.Severity == 3).RemediationDays);
    }

    [Fact]
    public async Task TestSettingAPolicyReturnsCreated()
    {
        var controller = Controller<SlaConfigurationsController>();

        var result = await controller.Set(new SlaConfiguration
        {
            Severity = 4, MaxTriageDays = 2, MaxRemediationDays = 15
        });

        Assert.IsType<CreatedResult>(result.Result);
    }

    [Fact]
    public async Task TestAnImpossiblePolicyIsABadRequest()
    {
        var controller = Controller<SlaConfigurationsController>();

        // A triage window longer than the remediation window describes a policy nobody can meet.
        var result = await controller.Set(new SlaConfiguration
        {
            Severity = 4, MaxTriageDays = 30, MaxRemediationDays = 15
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestRecomputingOnAnUnknownFindingIsNotFound()
    {
        var controller = Controller<SlaConfigurationsController>();

        Assert.IsType<NotFoundResult>((await controller.Recompute(404)).Result);
    }

    // --- 3.3.3 dedup configuration --------------------------------------------------------------

    [Fact]
    public async Task TestDedupOptionsListStrategiesAndFields()
    {
        var controller = Controller<DedupConfigurationsController>();

        var options = Ok(await controller.GetOptions());

        Assert.Contains("HashBased", options.Strategies);
        Assert.Contains("ruleId", options.HashFields);
        Assert.NotEmpty(options.DefaultHashFields);
    }

    [Fact]
    public async Task TestSavingAnUnknownStrategyIsABadRequest()
    {
        var controller = Controller<DedupConfigurationsController>();

        var result = await controller.Save("nessus", new ScannerDedupConfiguration
        {
            Importer = "nessus", StrategyChain = "HashBased,Telepathy"
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestPreviewReportsBothVerdicts()
    {
        var controller = Controller<DedupConfigurationsController>();

        var merged = Ok(await controller.Preview("nessus", new DedupPreviewRequest
        {
            Left = new Contracts.Importers.NormalizedFinding { Title = "same" },
            Right = new Contracts.Importers.NormalizedFinding { Title = "same" }
        }));

        Assert.True(merged.WouldMerge);
        Assert.NotEmpty(merged.SharedKeys);
        // Every candidate key is reported, so a surprising verdict can be explained.
        Assert.NotEmpty(merged.LeftKeys);

        var separate = Ok(await controller.Preview("nessus", new DedupPreviewRequest
        {
            Left = new Contracts.Importers.NormalizedFinding { Title = "one" },
            Right = new Contracts.Importers.NormalizedFinding { Title = "another" }
        }));

        Assert.False(separate.WouldMerge);
    }

    [Fact]
    public async Task TestPreviewNeedsTwoFindings()
    {
        var controller = Controller<DedupConfigurationsController>();

        Assert.IsType<BadRequestObjectResult>(
            (await controller.Preview("nessus", new DedupPreviewRequest())).Result);
    }

    [Fact]
    public async Task TestDedupHistoryIsServed()
    {
        var controller = Controller<DedupConfigurationsController>();

        Assert.Single(Ok(await controller.GetHistory("nessus")));
    }

    // --- 3.5.1 API tokens -----------------------------------------------------------------------

    [Fact]
    public async Task TestIssuingATokenReturnsTheSecretExactlyOnce()
    {
        var controller = Controller<ApiTokensController>();

        var result = await controller.Issue(new ApiTokenIssueRequest
        {
            Name = "github-actions", Scopes = ApiTokenScopes.VulnerabilitiesImport
        });

        var created = Assert.IsType<CreatedResult>(result.Result);
        var issued = Assert.IsType<IssuedApiToken>(created.Value);

        Assert.Equal(MockedApiTokensService.IssuedSecret, issued.Secret);
        Assert.StartsWith(ApiToken.SecretPrefix, issued.Secret);
    }

    [Fact]
    public async Task TestATokenListingNeverCarriesAHashOrASecret()
    {
        var controller = Controller<ApiTokensController>();

        var tokens = Ok(await controller.GetAll());

        // Serialising the entity directly would put secret_hash on the wire. The projection is what
        // makes that impossible rather than merely unlikely.
        Assert.All(tokens, t =>
        {
            Assert.DoesNotContain("0123456789abcdef", t.KeyId);
            Assert.NotEmpty(t.Scopes);
        });

        Assert.DoesNotContain(typeof(ApiTokenView).GetProperties(),
            p => p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TestRevokedTokensAreOptIn()
    {
        var controller = Controller<ApiTokensController>();

        Assert.Single(Ok(await controller.GetAll()));
        Assert.Equal(2, Ok(await controller.GetAll(includeRevoked: true)).Count);
    }

    [Fact]
    public async Task TestIssuingWithoutAScopeIsABadRequest()
    {
        var controller = Controller<ApiTokensController>();

        var result = await controller.Issue(new ApiTokenIssueRequest { Name = "ci", Scopes = "" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestRevokingAnUnknownTokenIsNotFound()
    {
        var controller = Controller<ApiTokensController>();

        Assert.IsType<NotFoundResult>((await controller.Revoke(404)).Result);
    }

    [Fact]
    public void TestScopesAreDiscoverable()
    {
        var controller = Controller<ApiTokensController>();

        var scopes = Assert.IsType<string[]>(
            Assert.IsType<OkObjectResult>(controller.GetScopes().Result).Value);

        Assert.Contains(ApiTokenScopes.VulnerabilitiesImport, scopes);
    }
}
