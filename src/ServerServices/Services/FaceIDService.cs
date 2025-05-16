using Contracts;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.FaceID;
using Model.Services;
using Serilog;
using ServerServices.Interfaces;
using SkiaSharp;
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

    public async Task SetUserEnabledStatusAsync(int userId, bool enabled)
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
                SignatureSeed = SeedGenerator.GenerateUniqueSeedBase64()
            };
            
            context.FaceIDUsers.Add(faceIdUser);
        }
        else
        {
            faceIdUser.IsEnabled = enabled;
            context.FaceIDUsers.Update(faceIdUser);
        }
        
        await context.SaveChangesAsync();
    }

    public async Task SaveFaceIdAsync(int userId, FaceData faceData)
    {
        if (!await IsFaceIDPluginEnabled()) return;
        
        var faceIdPlugin = await PluginsService.GetPluginAsync<INetriskFaceIDPlugin>("FaceIdPlugin");
        if (faceIdPlugin == null)
        {
            throw new Exception("FaceId plugin not found");
        }
        
        // Check if the user exists
        if(await UsersService.GetUserByIdAsync(userId) == null)
        {
            throw new UserNotFoundException($"User with id {userId} does not exist");
        }

        if(faceData.FaceDescriptorB64 == null) 
            throw new Exception("Face descriptor is null");
        
        var skFace = Base64Converter.FromBase64Json<SKBitmap>(faceData.FaceDescriptorB64);

        if (skFace == null)
        {
            throw new Exception("Face descriptor is null");
        }
        
        var face = faceIdPlugin.ExtractFace(skFace);
        
        if (face == null)
        {
            throw new Exception("Face descriptor is null");
        }

        var descriptor = faceIdPlugin.ExtractEncodings(face);
        
        // TODO SAVE THE FACE DESCRIPTOR 

    }
}