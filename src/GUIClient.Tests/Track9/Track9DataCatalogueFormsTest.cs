using System;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.DataCatalogue;
using Xunit;

namespace GUIClient.Tests.Track9;

public class Track9DataCatalogueFormsTest
{
    [Fact]
    public void TestBuildRequestKeepsPurposesLocationsRetentionAndTransfer()
    {
        var source = new DataRecordDto
        {
            PersonalData = PersonalDataCategory.SensitivePersonal,
            InvolvesMinors = true,
            LargeVolume = true,
            StrategicResearch = true,
            DataSubjects = "students",
            DataCategories = "health",
            RetentionPeriodMonths = 60,
            RetentionTrigger = "graduation",
            RetentionBasis = "sector rule",
            RetentionRequirement = new LegalRequirementRefDto { Id = 9 },
            RetentionReviewDueAt = new DateTime(2027, 1, 2),
            RetentionReviewedAt = new DateTime(2026, 1, 2),
            InternationalTransfer = true,
            TransferMechanism = InternationalTransferMechanism.StandardContractualClauses,
            Notes = "kind only",
            Purposes =
            [
                new DataCataloguePurposeDto
                {
                    Purpose = "enrolment",
                    LegalBasis = LgpdLegalBasis.Art11LegalObligation,
                    LegalRequirement = new LegalRequirementRefDto { Id = 10 },
                    BasisReference = "art. 1"
                }
            ],
            Locations =
            [
                new DataCatalogueLocationDto
                {
                    Country = "BR",
                    Region = "RJ",
                    Purpose = DataLocationPurpose.Storage
                }
            ]
        };

        var request = Track9DataCatalogueForms.BuildRequest(source);

        Assert.Equal(PersonalDataCategory.SensitivePersonal, request.PersonalData);
        Assert.True(request.InvolvesMinors);
        Assert.True(request.LargeVolume);
        Assert.True(request.StrategicResearch);
        Assert.Equal(60, request.RetentionPeriodMonths);
        Assert.Equal(9, request.RetentionRequirementId);
        Assert.True(request.InternationalTransfer);
        Assert.Equal(InternationalTransferMechanism.StandardContractualClauses, request.TransferMechanism);
        var purpose = Assert.Single(request.Purposes!);
        Assert.Equal(LgpdLegalBasis.Art11LegalObligation, purpose.LegalBasis);
        Assert.Equal(10, purpose.LegalRequirementId);
        var location = Assert.Single(request.Locations!);
        Assert.Equal(DataLocationPurpose.Storage, location.Purpose);
    }

    [Fact]
    public void TestUncataloguedRecordBuildsRequiredEmptyDeclarations()
    {
        var request = Track9DataCatalogueForms.BuildRequest(new DataRecordDto());

        Assert.NotNull(request.Purposes);
        Assert.Empty(request.Purposes);
        Assert.NotNull(request.Locations);
        Assert.Empty(request.Locations);
    }
}
