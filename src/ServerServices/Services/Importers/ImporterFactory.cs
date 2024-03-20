using ServerServices.Interfaces.Importers;

namespace ServerServices.Services.Importers;

public class ImporterFactory: IVulnerabilityImporterFactory
{
    public IVulnerabilityImporter GetImporter(string impoterType)
    {
        switch (impoterType.ToLower())
        {
            case "tenable nessus":
                return new NessusImporter();
            //case "secure scorecard":
            //    return new SecureScoreCardImporter();
            default:
                throw new Exception("Importer not found");
            
        }
    }
}