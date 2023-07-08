using Model.Authentication;

namespace ClientServices.Interfaces;

public interface IMutableConfigurationService
{
    bool IsInitialized { get; }

    void Initialize();

    string? GetConfigurationValue(string name);
    
    void SetConfigurationValue(string name, string value);

    void RemoveConfigurationValue(string name);
    
    void SaveAuthenticatedUser(AuthenticatedUserInfo user);
    
    AuthenticatedUserInfo? GetAuthenticatedUser();
}