namespace ClientServices.Interfaces;

public interface IEnvironmentService
{
    string NewLine { get; }

    bool Is64BitProcess { get; }
    
    string ApplicationData { get; }
    
    string ApplicationDataFolder { get; }
    
    string DeviceID { get; }
    
    public string DeviceToken { get; }

    string? GetEnvironmentVariable(string variableName);
}