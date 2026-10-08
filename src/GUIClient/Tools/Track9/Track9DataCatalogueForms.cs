using System.Linq;
using Model.DataCatalogue;

namespace GUIClient.Tools.Track9;

/// <summary>Typed request builders shared by the Stage 9.11 catalogue screens.</summary>
public static class Track9DataCatalogueForms
{
    public static DataCatalogueEntryRequest BuildRequest(DataRecordDto source) => new()
    {
        PersonalData = source.PersonalData,
        InvolvesMinors = source.InvolvesMinors,
        LargeVolume = source.LargeVolume,
        StrategicResearch = source.StrategicResearch,
        DataSubjects = source.DataSubjects,
        DataCategories = source.DataCategories,
        RetentionPeriodMonths = source.RetentionPeriodMonths,
        RetentionTrigger = source.RetentionTrigger,
        RetentionBasis = source.RetentionBasis,
        RetentionRequirementId = source.RetentionRequirement?.Id,
        RetentionReviewDueAt = source.RetentionReviewDueAt,
        RetentionReviewedAt = source.RetentionReviewedAt,
        InternationalTransfer = source.InternationalTransfer,
        TransferMechanism = source.TransferMechanism,
        Notes = source.Notes,
        Purposes = source.Purposes.Select(purpose => new DataCataloguePurposeRequest
        {
            Purpose = purpose.Purpose,
            LegalBasis = purpose.LegalBasis,
            LegalRequirementId = purpose.LegalRequirement?.Id,
            BasisReference = purpose.BasisReference
        }).ToList(),
        Locations = source.Locations.Select(location => new DataCatalogueLocationRequest
        {
            Country = location.Country,
            Region = location.Region,
            Purpose = location.Purpose
        }).ToList()
    };
}
