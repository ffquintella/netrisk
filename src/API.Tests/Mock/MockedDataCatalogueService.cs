using System.Collections.Generic;
using DAL.Entities;
using DAL.Enums;
using Model.DataCatalogue;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="IDataCatalogueService"/> for <c>DataCatalogueControllerTest</c> (S52 §6): the entity,
/// requirement, RIPD or risk id — every id the route has — drives the branch through <see cref="DecisionCycleIds.Gate"/>, so
/// every action answers each domain exception the same way the Stage 9.9 and 9.10 controllers do. The two creations have no
/// id in the route: a requirement is driven by the id in its <c>ThirdPartyId</c> and a RIPD by the number in its title.
/// </summary>
public static class MockedDataCatalogueService
{
    public static DataRecordDto Record(int entityId) => new()
    {
        EntityId = entityId, Name = "Student registry", Catalogued = true, PersonalData = PersonalDataCategory.Personal,
        InvolvesMinors = true, DataSubjects = "Students", DataCategories = "Enrolment data", InternationalTransfer = true,
        TransferMechanism = InternationalTransferMechanism.StandardContractualClauses,
        Purposes =
        [
            new DataCataloguePurposeDto
                { Id = 1, Purpose = "Enrolment", LegalBasis = LgpdLegalBasis.Art7Contract, LegalBasisArticle = "LGPD art. 7º, V" }
        ],
        Locations = [new DataCatalogueLocationDto { Id = 1, Country = "US", Purpose = DataLocationPurpose.Storage }],
        TransferCountries = ["US"],
        Findings = [new DataCatalogueFindingDto { Code = DataCatalogueFindingCode.DpiaMissing, Message = "No approved RIPD." }]
    };

    public static LegalRequirementDto Requirement(int id) => new()
    {
        Id = id, Code = "LGPD-7", Title = "Legal bases", Kind = LegalRequirementKind.Law, RiskLinkCount = 2, PurposeCount = 3
    };

    public static DpiaDto Dpia(int id) => new()
    {
        Id = id, Title = "Enrolment RIPD", Status = DpiaStatus.Draft, ResidualRisk = DpiaResidualRisk.Medium,
        Links = [new DpiaLinkDto { EntityId = 20, Kind = DpiaLinkKind.DataRecord, EntityName = "Student registry" }]
    };

    public static RiskComplianceDto Compliance(int riskId) => new()
    {
        RiskId = riskId,
        Requirements = [new RiskRequirementDto { RequirementId = 8, Code = "LGPD-7", Title = "Legal bases", Kind = LegalRequirementKind.Law }],
        DataRecords = [new RiskDataRecordDto { EntityId = 20, Name = "Student registry", Catalogued = true }],
        CatalogueRequirements = [new LegalRequirementRefDto { Id = 9, Code = "LGPD-16", Title = "Retention", Kind = LegalRequirementKind.Law }]
    };

    private static List<AuditLog> History(string entityType, int id) =>
        [new AuditLog { Id = 1, EntityType = entityType, EntityId = id, Field = string.Empty, Actor = "user:1" }];

    public static IDataCatalogueService Create()
    {
        var service = Substitute.For<IDataCatalogueService>();

        service.GetRecordsAsync(Arg.Any<bool>()).Returns(_ => new List<DataRecordSummaryDto>
        {
            new()
            {
                EntityId = DecisionCycleIds.Known, Name = "Student registry", Catalogued = true,
                PersonalData = PersonalDataCategory.Personal, PurposeCount = 1,
                FindingCodes = [DataCatalogueFindingCode.DpiaMissing]
            }
        });
        service.GetRecordAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return Record(call.Arg<int>());
        });
        service.GetRecordHistoryAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return History(nameof(DataCatalogueEntry), call.ArgAt<int>(0));
        });
        service.SaveRecordAsync(Arg.Any<int>(), Arg.Any<DataCatalogueEntryRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Record(call.ArgAt<int>(0));
        });

        service.GetRequirementsAsync().Returns(_ => new List<LegalRequirementDto> { Requirement(DecisionCycleIds.Known) });
        service.GetRequirementHistoryAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return History(nameof(LegalRequirement), call.ArgAt<int>(0));
        });
        service.CreateRequirementAsync(Arg.Any<LegalRequirementRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<LegalRequirementRequest>().ThirdPartyId ?? 0);
            return Requirement(DecisionCycleIds.Known);
        });
        service.UpdateRequirementAsync(Arg.Any<int>(), Arg.Any<LegalRequirementRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Requirement(call.ArgAt<int>(0));
        });
        service.DeleteRequirementAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return System.Threading.Tasks.Task.CompletedTask;
        });

        service.GetDpiasAsync(Arg.Any<DpiaStatus?>()).Returns(_ => new List<DpiaSummaryDto>
        {
            new() { Id = DecisionCycleIds.Known, Title = "Enrolment RIPD", Status = DpiaStatus.Approved, DataRecordCount = 1 }
        });
        service.GetDpiaAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return Dpia(call.Arg<int>());
        });
        service.GetDpiaHistoryAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return History(nameof(Dpia), call.ArgAt<int>(0));
        });
        service.CreateDpiaAsync(Arg.Any<DpiaRequest>(), Arg.Any<int>()).Returns(call =>
        {
            if (int.TryParse(call.Arg<DpiaRequest>().Title, out var drivingId))
                DecisionCycleIds.Gate(drivingId);
            return Dpia(DecisionCycleIds.Known);
        });
        service.UpdateDpiaAsync(Arg.Any<int>(), Arg.Any<DpiaRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Dpia(call.ArgAt<int>(0));
        });
        service.LinkDpiaAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return Dpia(call.ArgAt<int>(0));
        });
        service.UnlinkDpiaAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return System.Threading.Tasks.Task.CompletedTask;
        });
        service.ApproveDpiaAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            var dpia = Dpia(call.ArgAt<int>(0));
            dpia.Status = DpiaStatus.Approved;
            return dpia;
        });
        service.RetireDpiaAsync(Arg.Any<int>(), Arg.Any<DpiaRetireRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            var dpia = Dpia(call.ArgAt<int>(0));
            dpia.Status = DpiaStatus.Retired;
            return dpia;
        });

        service.GetRiskComplianceAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return Compliance(call.Arg<int>());
        });
        service.LinkRiskRequirementAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<RiskLegalRequirementRequest>(), Arg.Any<int>())
            .Returns(call =>
            {
                DecisionCycleIds.Gate(call.ArgAt<int>(0));
                DecisionCycleIds.Gate(call.ArgAt<int>(1));
                return Compliance(call.ArgAt<int>(0));
            });
        service.UnlinkRiskRequirementAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return System.Threading.Tasks.Task.CompletedTask;
        });

        return service;
    }
}
