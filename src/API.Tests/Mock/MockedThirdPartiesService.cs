using System.Collections.Generic;
using DAL.Entities;
using DAL.Enums;
using Model.ThirdParties;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="IThirdPartiesService"/> for <c>ThirdPartiesControllerTest</c> (S51 §8): the third-party id —
/// or the entity, assessment or SBOM id where the route has one — drives the branch through
/// <see cref="DecisionCycleIds.Gate"/>, so every action answers each domain exception the same way the Stage 9.9
/// controllers do.
/// </summary>
public static class MockedThirdPartiesService
{
    public static ThirdPartyDto Party(int id) => new()
    {
        Id = id, Name = "Acme Cloud", Status = ThirdPartyStatus.Active, IsCloudProvider = true,
        Links = [new ThirdPartyLinkDto { EntityId = 20, Kind = ThirdPartyLinkKind.ItService, EntityName = "Student portal" }],
        Hecvat = new HecvatResultDto { State = HecvatState.Incomplete, ExpectedCount = 4, AnsweredCount = 3 },
        Concentration = new ConcentrationEntryDto { ThirdPartyId = id, Name = "Acme Cloud", DependentCriticalProcessCount = 2 },
        Findings = [new ThirdPartyFindingDto { Code = ThirdPartyFindingCode.HecvatIncomplete, Message = "Incomplete." }]
    };

    public static ThirdPartyAssessmentDto Assessment(int thirdPartyId, int id) => new()
    {
        Id = id, ThirdPartyId = thirdPartyId, Variant = HecvatVariant.Full, FrameworkVersion = "3.06", ExpectedQuestionCount = 4,
        Result = new HecvatResultDto { State = HecvatState.Incomplete, ExpectedCount = 4, AnsweredCount = 3 },
        Answers = [new HecvatAnswerDto { QuestionId = "HFIH-01", Answer = HecvatAnswer.Yes, PreferredAnswer = HecvatAnswer.Yes, Weight = 1 }]
    };

    public static ThirdPartySbomDto Sbom(int thirdPartyId, int id) => new()
    {
        Id = id, ThirdPartyId = thirdPartyId, ComponentName = "Moodle LMS", Format = SbomFormat.CycloneDxJson,
        DocumentSha256 = new string('a', 64), ComponentCount = 1,
        Components = [new SbomComponentDto { Name = "log4j-core", Version = "2.17.1" }]
    };

    public static IThirdPartiesService Create()
    {
        var service = Substitute.For<IThirdPartiesService>();

        service.GetThirdPartiesAsync(Arg.Any<ThirdPartyStatus?>(), Arg.Any<bool>()).Returns(_ => new List<ThirdPartySummaryDto>
        {
            new() { Id = DecisionCycleIds.Known, Name = "Acme Cloud", Status = ThirdPartyStatus.Active, HecvatState = HecvatState.Conforming }
        });
        service.GetThirdPartyAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return Party(call.Arg<int>());
        });
        service.GetConcentrationAsync().Returns(_ => new ThirdPartyConcentrationReportDto
        {
            CriticalProcessCount = 2,
            Suppliers = new ConcentrationDimensionDto
            {
                Dimension = ConcentrationDimension.Supplier, ProviderCount = 1, MaxShare = 1m,
                MostConcentratedThirdPartyId = DecisionCycleIds.Known,
                Entries = [new ConcentrationEntryDto { ThirdPartyId = DecisionCycleIds.Known, Name = "Acme Cloud", DependentCriticalProcessCount = 2, Share = 1m }]
            }
        });
        service.GetByEntityAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return new List<EntityThirdPartyDto>
            {
                new() { ThirdPartyId = DecisionCycleIds.Known, Name = "Acme Cloud", Kind = ThirdPartyLinkKind.Data }
            };
        });
        service.GetAssessmentAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return Assessment(call.ArgAt<int>(0), call.ArgAt<int>(1));
        });
        service.GetSbomAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return Sbom(call.ArgAt<int>(0), call.ArgAt<int>(1));
        });
        service.GetHistoryAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return new List<AuditLog>
            {
                new() { Id = 1, EntityType = nameof(ThirdParty), EntityId = call.ArgAt<int>(0), Field = string.Empty, Actor = "user:1" }
            };
        });

        service.CreateAsync(Arg.Any<ThirdPartyRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<ThirdPartyRequest>().EntityId ?? 0);
            return Party(DecisionCycleIds.Known);
        });
        service.UpdateAsync(Arg.Any<int>(), Arg.Any<ThirdPartyRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Party(call.ArgAt<int>(0));
        });
        service.DeleteAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return System.Threading.Tasks.Task.CompletedTask;
        });
        service.LinkAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ThirdPartyLinkRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return Party(call.ArgAt<int>(0));
        });
        service.UnlinkAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return System.Threading.Tasks.Task.CompletedTask;
        });
        service.SetSubprocessorsAsync(Arg.Any<int>(), Arg.Any<ThirdPartySubprocessorsRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Party(call.ArgAt<int>(0));
        });
        service.SetDataLocationsAsync(Arg.Any<int>(), Arg.Any<ThirdPartyDataLocationsRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Party(call.ArgAt<int>(0));
        });
        service.RecordAssessmentAsync(Arg.Any<int>(), Arg.Any<ThirdPartyAssessmentRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Assessment(call.ArgAt<int>(0), DecisionCycleIds.Known);
        });
        service.ReplaceAnswersAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ThirdPartyAssessmentAnswersRequest>(), Arg.Any<int>())
            .Returns(call =>
            {
                DecisionCycleIds.Gate(call.ArgAt<int>(0));
                DecisionCycleIds.Gate(call.ArgAt<int>(1));
                return Assessment(call.ArgAt<int>(0), call.ArgAt<int>(1));
            });
        service.VoidAssessmentAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ThirdPartyAssessmentVoidRequest>(), Arg.Any<int>())
            .Returns(call =>
            {
                DecisionCycleIds.Gate(call.ArgAt<int>(0));
                DecisionCycleIds.Gate(call.ArgAt<int>(1));
                var assessment = Assessment(call.ArgAt<int>(0), call.ArgAt<int>(1));
                assessment.Result.State = HecvatState.Voided;
                return assessment;
            });
        service.ImportSbomAsync(Arg.Any<int>(), Arg.Any<ThirdPartySbomRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Sbom(call.ArgAt<int>(0), DecisionCycleIds.Known);
        });
        service.DeleteSbomAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return System.Threading.Tasks.Task.CompletedTask;
        });

        return service;
    }
}
