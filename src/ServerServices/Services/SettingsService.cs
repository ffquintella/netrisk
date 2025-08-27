using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class SettingsService(ILogger logger, IDalService dalService):  ServiceBase(logger, dalService), ISettingsService
{
    public async void ChangeBackupPasswordAsync(string password)
    {
        if (password == null || password == "")
        {
            Logger.Error("Please enter the password!");
            throw new InvalidParameterException("password", "Please enter the password!");
        }
        
        // Change the password
        await using var dbContext = DalService.GetContext();
        
        var bkp_pwd  = await dbContext.Settings.FirstOrDefaultAsync(x => x.Name == "BackupPassword");

        if (bkp_pwd == null)
        {
            Log.Error("Backup password not found in the database!");
            return;
        }
        
        bkp_pwd.Value = password;
        await dbContext.SaveChangesAsync();
    }

    public async Task<bool> ConfigurationKeyExistsAsync(string key)
    {
        await using var dbContext = DalService.GetContext();
        
        var config = await dbContext.Settings.FirstOrDefaultAsync(x => x.Name == key);
        if (config == null)
        {
            return false;
        }
        
        return true;
    }

    public async Task<string> GetConfigurationKeyValueAsync(string key)
    {
        if (await ConfigurationKeyExistsAsync(key))
        {
            await using var dbContext = DalService.GetContext();
            
            var config = await dbContext.Settings.FirstOrDefaultAsync(x => x.Name == key);
            if (config == null)
            {
                throw new Exception($"The key {key} was not found!");
            }
            
            if (config.Value == null) 
            {
                throw new Exception($"The key {key} has no value!");
            }
            
            return config.Value;
        }
        else
        {
            throw new Exception($"The key {key} was not found!");
        }
    }

    public async Task SetConfigurationKeyValueAsync(string key, string value)
    {
        await using var dbContext = DalService.GetContext();
        if (await ConfigurationKeyExistsAsync(key))
        {
            var config = await dbContext.Settings.FirstOrDefaultAsync(x => x.Name == key);
            if (config == null)
            {
                throw new Exception($"The key {key} was not found!");
            }
            
            config.Value = value;
            await dbContext.SaveChangesAsync();
        }
        else
        {
            var config = new Setting
            {
                Name = key,
                Value = value
            };
            
            dbContext.Settings.Add(config);
            await dbContext.SaveChangesAsync();
            
        }
    }
}