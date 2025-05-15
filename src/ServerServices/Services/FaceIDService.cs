using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Services;
using Serilog;
using ServerServices.Interfaces;

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
    
    public async Task<ServiceInformation> GetInfoAsync()
    {
        
        var faceIDPluginExists = await PluginsService.PluginExistsAsync("FaceIdPlugin");
        
        bool faceIdPluginEnabled = false;
        if (faceIDPluginExists)
        {
            faceIdPluginEnabled = await PluginsService.PluginIsEnabledAsync("FaceIdPlugin");
        }
        
        
        var information = new ServiceInformation
        {
            IsServiceAvailable = faceIdPluginEnabled,
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
        // Check if the user exists
        if(UsersService.GetUserByIdAsync(userId) == null)
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
        // Check if the user exists
        if(UsersService.GetUserByIdAsync(userId) == null)
        {
            throw new UserNotFoundException($"User with id {userId} does not exist");
        }
        
        await using var context = DalService.GetContext();
        
        var user = await context.FaceIDUsers
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (user == null)
        {
            // If it does not exist, create it
            user = new FaceIDUser
            {
                UserId = userId,
                IsEnabled = enabled,
                SignatureSeed = SeedGenerator.GenerateUniqueSeedBase64()
            };
        }
        else
        {
            user.IsEnabled = enabled;
            context.FaceIDUsers.Update(user);
        }
        
        await context.SaveChangesAsync();
    }
}