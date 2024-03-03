using System;
using System.Collections.Generic;
using Model.Authentication;


namespace ClientServices.Interfaces;

/// <summary>
/// A service for managing the authentication of the user.
/// </summary>
public interface IAuthenticationService
{
    bool IsAuthenticated { get; set; }
    
    AuthenticationCredential AuthenticationCredential { get; set; }
    AuthenticatedUserInfo? AuthenticatedUserInfo { get; set; }
    
    /// <summary>
    /// Try to authenticate the user with the server.
    /// </summary>
    /// <returns></returns>
    bool TryAuthenticate();
    
    /// <summary>
    /// Try to authenticate the user with the server.
    /// </summary>
    /// <returns></returns>
    public Task<bool> TryAuthenticateAsync();
    
    /// <summary>
    /// Gets the list of authentication methods avaliable at the server.
    /// </summary>
    /// <returns> A list of authentication methods</returns>
    List<AuthenticationMethod> GetAuthenticationMethods();
    
    /// <summary>
    /// Authenticates the user with the given credentials.
    /// </summary>
    /// <param name="user"> Username</param>
    /// <param name="password"> User password</param>
    /// <returns>0 if success; -1 if unkown error; 1 if authentication error</returns>
    int DoServerAuthentication(string user, string password);


    /// <summary>
    /// Logout the user
    /// </summary>
    void Logout();
    
    /// <summary>
    ///  Gets the information about the authenticated user from the server.
    /// </summary>
    /// <returns>0 if success; -1 if internal error; 1 if communication error;</returns>
    int GetAuthenticatedUserInfo();

    /// <summary>
    /// Checks if the auntetication token is still valid
    /// </summary>
    /// <param name="token">the value of the token</param>
    /// <param name="minutesToExpire">add times to check if it will be valid in the futre</param>
    /// <returns>true of false</returns>
    bool CheckTokenValidTime(string token, int minutesToExpire = 0);


    /// <summary>
    /// Checks if the SAML authentication process is successful
    /// </summary>
    /// <param name="requestId"></param>
    /// <returns>true if ok; false if not</returns>
    bool CheckSamlAuthentication(string requestId);
    int RefreshToken();


    /// <summary>
    /// Discards the authentication token
    /// </summary>
    void DiscardAuthenticationToken();

    /// <summary>
    /// Notifies the authentication service that the authentication has succeeded.
    /// </summary>
    public void NotifyAuthenticationSucceeded();
    
    event EventHandler? AuthenticationSucceeded;
}