using Model.Services;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class FaceIDService: ServiceBase, IFaceIDService
{
    
    private IPluginsService PluginsService { get; }
    
    public FaceIDService(ILogger logger, IDalService dalService, IPluginsService pluginsService) : base(logger, dalService)
    {
        PluginsService = pluginsService;
    }
    
    public async Task<ServiceInformation> GetInfoAsync()
    {
        
        var faceIDPluginExists = PluginsService.PluginExists("FaceID");
        
        bool faceIdPluginEnabled = false;
        if (faceIDPluginExists)
        {
            faceIdPluginEnabled = await PluginsService.PluginIsEnabledAsync("FaceID");
        }
        
        
        var information = new ServiceInformation
        {
            IsServiceAvailable = faceIdPluginEnabled,
            ServiceName = "FaceID",
            ServiceVersion = "1.0",
            ServiceDescription = "FaceID service for user authentication",
            ServiceUrl = "/faceid",
            ServiceNeedsPlugin = true,
            ServicePluginInstalled = faceIDPluginExists
        };

        return information;
        
    }
}