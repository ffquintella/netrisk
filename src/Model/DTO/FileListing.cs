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
    
    public string Name { get; set; } = "";
    public string UniqueName { get; set; } = "";
    public string? Type { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int OwnerId { get; set; }
}