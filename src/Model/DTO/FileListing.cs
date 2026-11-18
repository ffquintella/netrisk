using Model.File;

namespace Model.DTO;

public class FileListing
{
    
    public FileListing()
    {
    }
    
    /*public FileListing(DAL.Entities.NrFile file)
    {
        Name = file.Name;
        UniqueName = file.UniqueName;
        Type = file.Type;
        Timestamp = file.Timestamp;

        if (file.ViewType != null)
        {
            switch (file.ViewType.Value)
            {
                case (int)FileCollectionType.MitigationFile:
                    OwnerId = file.MitigationId!.Value;
                    break;
                case (int)FileCollectionType.RiskFile:
                    OwnerId = file.RiskId!.Value;
                    break;
                case (int)FileCollectionType.IncidentResponsePlanFile:
                    OwnerId = file.IncidentResponsePlanId!.Value;
                    break;
                case (int)FileCollectionType.IncidentResponsePlanTaskFile:
                    OwnerId = file.IncidentResponsePlanTaskId!.Value;
                    break;
                case (int)FileCollectionType.IncidentFile:
                    OwnerId = file.IncidentId!.Value;
                    break;
            }
        }


    }*/
    
    /// <summary>
    /// The stored row's id, filled by the upload (null on the listing routes). The committee minutes are linked to a profile
    /// by id (<c>POST /RiskIndex/Profiles/{id}/Attachments</c>, S40 §8.4), so the uploader needs it back (T262).
    /// </summary>
    public int? Id { get; set; }

    public string Name { get; set; } = "";
    public string UniqueName { get; set; } = "";
    public string? Type { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int OwnerId { get; set; }
}