using Model.Services;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class FaceIDService: ServiceBase, IFaceIDService
{
    
    public FaceIDService(ILogger logger, IDalService dalService) : base(logger, dalService)
    {
        
    }
    
    public ServiceInformation GetInfo()
    {
        var information = new ServiceInformation
        {
            IsServiceAvailable = true,
            ServiceName = "FaceID",
            ServiceVersion = "1.0",
            ServiceDescription = "FaceID service for user authentication",
            ServiceUrl = "/faceid",
            ServiceNeedsPlugin = true,
            ServicePluginInstalled = false
        };

        return information;
        
    }
}