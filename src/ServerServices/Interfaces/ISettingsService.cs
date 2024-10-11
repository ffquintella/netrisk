namespace ServerServices.Interfaces;

public interface ISettingsService
{
    /// <summary>
    /// Change the backup password
    /// </summary>
    /// <param name="password"></param>
    public void ChangeBackupPasswordAsync(string password);
}