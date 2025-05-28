using System.Text.Json;
using Contracts;
using Contracts.Exceptions;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.FaceID;
using Model.Services;
using Serilog;
using ServerServices.Interfaces;
using SkiaSharp;
using Tools.Extensions;
using Tools.Serialization;

namespace ServerServices.Services;

public class FaceIDService: ServiceBase, IFaceIDService
{
    
    private IPluginsService PluginsService { get; }
    private IUsersService UsersService { get; }
    
    public FaceIDService(ILogger logger, IDalService dalService, IPluginsService pluginsService, IUsersService usersService) : base(logger, dalService)
    {
        PluginsService = pluginsService;
        UsersService = usersService;
    }
    
    private async Task<bool> IsFaceIDPluginEnabled()
    {
        var faceIdPluginExists = await PluginsService.PluginExistsAsync("FaceIdPlugin");
        
        bool faceIdPluginEnabled = false;
        if (faceIdPluginExists)
        {
            faceIdPluginEnabled = await PluginsService.PluginIsEnabledAsync("FaceIdPlugin");
        }

        return faceIdPluginEnabled;
    }
    
    public async Task<ServiceInformation> GetInfoAsync()
    {
        
        var faceIDPluginExists = await PluginsService.PluginExistsAsync("FaceIdPlugin");
        
        
        var information = new ServiceInformation
        {
            IsServiceAvailable = await IsFaceIDPluginEnabled(),
            ServiceName = "FaceId",
            ServiceVersion = "1.0",
            ServiceDescription = "FaceID service for user authentication",
            ServiceUrl = "/faceid",
            ServiceNeedsPlugin = true,
            ServicePluginInstalled = faceIDPluginExists
        };

        return information;
        
    }

    public async Task<bool> IsUserEnabledAsync(int userId)
    {
        
        if(!await IsFaceIDPluginEnabled()) 
            return false;
        
        // Check if the user exists
        if(await UsersService.GetUserByIdAsync(userId) == null)
        {
            throw new UserNotFoundException($"User with id {userId} does not exist");
        }
        
        await using var context = DalService.GetContext();
        
        var user = await context.FaceIDUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);
        if (user == null)
        {
            return false;
        }
        return user.IsEnabled;
        
    }

    public async Task SetUserEnabledStatusAsync(int userId, bool enabled, int loggedUserId)
    {
        if (!await IsFaceIDPluginEnabled()) return;
        
        // Check if the user exists
        if(await UsersService.GetUserByIdAsync(userId) == null)
        {
            throw new UserNotFoundException($"User with id {userId} does not exist");
        }
        
        await using var context = DalService.GetContext();
        
        var faceIdUser = await context.FaceIDUsers
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (faceIdUser == null)
        {
            // If it does not exist, create it
            faceIdUser = new FaceIDUser
            {
                UserId = userId,
                IsEnabled = enabled,
                SignatureSeed = SeedGenerator.GenerateUniqueSeedBase64(),
                LastUpdateUserId = loggedUserId,
                LastUpdate = DateTime.Now
            };
            
            context.FaceIDUsers.Add(faceIdUser);
        }
        else
        {
            faceIdUser.IsEnabled = enabled;
            faceIdUser.LastUpdate = DateTime.Now;
            faceIdUser.LastUpdateUserId = loggedUserId;
            context.FaceIDUsers.Update(faceIdUser);
        }
        
        await context.SaveChangesAsync();
    }

    public async Task<string> SaveFaceIdAsync(int userId, FaceData faceData,  int loggedUserId)
    {
        if(faceData.ImageType != "jpg" && faceData.ImageType != "jpeg" &&  faceData.ImageType != "SKBitmap")
        {
            throw new Exception("Face image must be a SKBitmap, jpg or jpeg");
        }

        
        if (!await IsFaceIDPluginEnabled()) return "";
        
        var faceIdPlugin = await PluginsService.GetPluginAsync<INetriskFaceIDPlugin>("FaceIdPlugin");
        if (faceIdPlugin == null)
        {
            throw new Exception("FaceId plugin not found");
        }
        
        //Initializing the plugin 
        faceIdPlugin.Initialize(Logger);
        
        // Check if the user exists
        if(await UsersService.GetUserByIdAsync(userId) == null)
        {
            throw new UserNotFoundException($"User with id {userId} does not exist");
        }

        //Check if the user has faceid enabled
        if (!await IsUserEnabledAsync(userId))
        {
            throw new UserNotFoundException($"User with id {userId} has not been enabled");
        }

        SKBitmap? skFace = null;
        
        // Face image is a base64 encoded jpg
        if(faceData.FaceImageB64 == null) 
            throw new Exception("Face image is null");
        
        if(faceData.ImageType == "SKBitmap")
        {
            if(faceData.FaceImageB64 == null) throw new Exception("Face image is null");
            skFace = JsonSerializer.Deserialize<SKBitmap>(faceData.FaceImageJson);
        }
        else
        {
            byte[] imageBytes = Convert.FromBase64String(faceData.FaceImageB64);
       
            using var stream = new SKMemoryStream(imageBytes);
            skFace =  SKBitmap.Decode(stream);
        }
            
        

        if (skFace == null)
        {
            throw new Exception("Face descriptor is null");
        }
        
        var face = faceIdPlugin.ExtractFace(skFace);
        
        if (face == null)
        {
            throw new FaceDetectionException("Face descriptor is null");
        }

        var descriptor = faceIdPlugin.ExtractEncodings(face);
        
        if(descriptor == null) 
            throw new FaceDetectionException("Face descriptor is null");

        await using var context = DalService.GetContext();

        var descriptor64 = descriptor.ToBase64();
        
        // Let's get the faceid user 
        
        var faceIdUser = await context.FaceIDUsers
            .FirstOrDefaultAsync(x => x.UserId == userId);
        
        if (faceIdUser == null) throw new UserNotFoundException($"User with id {userId} has not been enabled");
        
        faceIdUser.FaceIdentification = descriptor64;
        faceIdUser.LastUpdate = DateTime.Now;
        faceIdUser.LastUpdateUserId = loggedUserId;
        
        await context.SaveChangesAsync();
        
        return descriptor64;

    }
}