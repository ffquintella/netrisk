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
}