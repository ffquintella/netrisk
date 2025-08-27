using System;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Microsoft.Extensions.Logging;
using Model.Services;

namespace GUIClient.Tools;

public class PluginManager
{
    #region PROPERTIES
    private ILogger Logger { get; }
    private IPluginsService PluginsService { get; }
    
    private IFaceIDService FaceIdService { get; }
    private IAuthenticationService AuthenticationService { get; }
    
    private IMemoryCacheService MemoryCacheService { get; }
    
    #endregion
    
    
    #region CONSTRUCTOR 
    public PluginManager(ILoggerFactory loggerFactory, 
        IPluginsService pluginsService, 
        IAuthenticationService authenticationService,
        IFaceIDService faceIdService,
        IMemoryCacheService memoryCacheService)
    {
        Logger = loggerFactory.CreateLogger(GetType());
        Logger.LogDebug("PluginManager initialized");
        
        PluginsService = pluginsService;
        AuthenticationService = authenticationService;
        FaceIdService = faceIdService;
        MemoryCacheService = memoryCacheService;
    }
    
    #endregion
    
    #region METHODS
    
    public async Task<bool> IsFaceIdEnabledAsync()
    {
        try
        {
            if(MemoryCacheService.HasCache<ServiceInformation>("FaceIdInfo"))
            {
                var cachedInfo = MemoryCacheService.Get<ServiceInformation>("FaceIdInfo");
                return cachedInfo!.IsServiceAvailable;
            }
            
            var isFaceIdInfo = await FaceIdService.GetInfo();
            
            MemoryCacheService.Set("FaceIdInfo", isFaceIdInfo, TimeSpan.FromHours(1));
                
            return isFaceIdInfo.IsServiceAvailable;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking if Face ID is enabled");
            return false;
        }
    }
    
    #endregion
}