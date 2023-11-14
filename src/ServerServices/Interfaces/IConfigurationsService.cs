namespace ServerServices.Interfaces;

public interface IConfigurationsService
{
    public void UpdateBackupPassword(string password);
    
    public string GetBackupPassword();
}