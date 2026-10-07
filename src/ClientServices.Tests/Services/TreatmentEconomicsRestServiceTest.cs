using System;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.TreatmentEconomics;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.6 (S47 §7, §8) — <see cref="TreatmentEconomicsRestService"/> over <see cref="StubRestBackend"/>, so every URL it
/// builds and every status branch runs for real. A Gate A refusal of "accept" keeps the server's sentence.
/// </summary>
[TestSubject(typeof(TreatmentEconomicsRestService))]
public class TreatmentEconomicsRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly ITreatmentEconomicsService _service;

    public TreatmentEconomicsRestServiceTest()
    {
        _service = ResolveWith<ITreatmentEconomicsService>(_backend);
    }

    private static MitigationEconomicsDto Economics(GateCOutcome outcome) => new()
    {
        MitigationId = 10, RiskId = 4, Declared = true, Option = TreatmentOption.TransferShare,
        TransferCounterparty = "Insurer", Cost = new TreatmentCostDto { Annual = 12_000, AnnualizedTotal = 12_000 },
        GateC = new GateCResultDto
        {
            Outcome = outcome,
            NotAssessableReasons = outcome == GateCOutcome.NotAssessable ? [GateCNotAssessableReason.NoMonetaryCost] : []
        }
    };

    [Fact]
    public async Task TestTheEconomicsAreReadAndWrittenWithTheirBodies()
    {
        _backend.OnGet("/TreatmentEconomics/Mitigations/10", Economics(GateCOutcome.NotAssessable));
        _backend.OnPut("/TreatmentEconomics/Mitigations/10", Economics(GateCOutcome.Passes));

        var read = await _service.GetMitigationAsync(10);
        Assert.Equal(GateCNotAssessableReason.NoMonetaryCost, Assert.Single(read.GateC.NotAssessableReasons));

        var saved = await _service.SaveMitigationAsync(10, new MitigationEconomicsRequest
        {
            Option = TreatmentOption.TransferShare, TransferCounterparty = "Insurer",
            Cost = new TreatmentCostRequest { OneTime = 0, Annual = 12_000, SideEffectsAnnual = 0 },
            PrerequisiteMitigationIds = [11]
        });

        Assert.Equal((TreatmentOption.TransferShare, GateCOutcome.Passes), (saved.Option!.Value, saved.GateC.Outcome));
        Assert.True(_backend.Sent(Method.Put, "/TreatmentEconomics/Mitigations/10"));
        Assert.Contains("\"transferCounterparty\":\"Insurer\"", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"prerequisiteMitigationIds\":[11]", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestTheRiskViewTheTargetAndItsRemoval()
    {
        _backend.OnGet("/TreatmentEconomics/Risks/4", new RiskTreatmentEconomicsDto
        {
            RiskId = 4, Tail = true, GateA = true, GateAConditions = [RiskFlagCode.KnownExploitation],
            Mitigations = [Economics(GateCOutcome.Fails)]
        });
        _backend.OnPut("/TreatmentEconomics/Risks/4/Target", new RiskTargetDto
        {
            RiskId = 4, TargetScore = 3, TargetDate = new DateOnly(2027, 3, 31), Rationale = "MFA.",
            Status = new RiskTargetStatusDto { ScoreMet = false, ScoreGap = 2, WithinAppetite = true }
        });
        _backend.OnStatus(Method.Delete, "/TreatmentEconomics/Risks/4/Target", HttpStatusCode.NoContent);

        var view = await _service.GetRiskAsync(4);
        Assert.Equal((true, RiskFlagCode.KnownExploitation), (view.Tail, Assert.Single(view.GateAConditions)));

        var target = await _service.SaveTargetAsync(4, new RiskTargetRequest
            { TargetScore = 3, TargetDate = new DateOnly(2027, 3, 31), Rationale = "MFA." });
        Assert.Equal((new DateOnly(2027, 3, 31), true), (target.TargetDate!.Value, target.Status.WithinAppetite!.Value));
        Assert.Contains("\"targetDate\":\"2027-03-31\"", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);

        await _service.DeleteTargetAsync(4);
        Assert.True(_backend.Sent(Method.Delete, "/TreatmentEconomics/Risks/4/Target"));
    }

    [Fact]
    public async Task TestThePortfolioSendsItsConstraints()
    {
        _backend.OnPost("/TreatmentEconomics/Portfolio", new PortfolioSelectionDto
        {
            Budget = 50_000, BudgetUsed = 15_000, ProtectedShortfall = false,
            Items = [new PortfolioItemDto { MitigationId = 11, Tier = PortfolioTier.Protected, Status = PortfolioItemStatus.Selected }]
        });

        var selection = await _service.SelectPortfolioAsync(new PortfolioSelectionRequest
            { Budget = 50_000, PeopleCapacityPersonDays = 20, Deadline = new DateOnly(2027, 1, 1) });

        Assert.Equal((PortfolioTier.Protected, PortfolioItemStatus.Selected),
            (Assert.Single(selection.Items).Tier, selection.Items[0].Status));
        Assert.Contains("\"budget\":50000", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"deadline\":\"2027-01-01\"", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestA404IsNotFound()
    {
        _backend.OnStatus(Method.Get, "/TreatmentEconomics/Mitigations/99", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetMitigationAsync(99));
    }

    /// <summary>A Gate A refusal of "accept", a cycle or a validation error reaches the person with the server's sentence.</summary>
    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "This risk cannot be accepted: it carries a non-discretionary (Gate A) condition.")]
    [InlineData(HttpStatusCode.BadRequest, "A transfer or share names its counterparty.")]
    public async Task TestARefusalKeepsTheServersSentence(HttpStatusCode status, string sentence)
    {
        _backend.OnPut("/TreatmentEconomics/Mitigations/10", new { error = "gate_a_non_discretionary", message = sentence }, status);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.SaveMitigationAsync(10, new MitigationEconomicsRequest { Option = TreatmentOption.Accept }));

        Assert.Contains(sentence, ex.Message);
    }

    [Fact]
    public async Task TestAServerErrorIsAGenericFailure()
    {
        _backend.OnStatus(Method.Post, "/TreatmentEconomics/Portfolio", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.SelectPortfolioAsync(new PortfolioSelectionRequest { Budget = 1 }));
    }
}
