using System;
using System.Collections.Generic;
using Model.Authentication;
using Model.FaceID;


namespace ClientServices.Interfaces;

/// <summary>
/// A service for managing the authentication of the user.
/// </summary>
public interface IAuthenticationService
{
    bool IsAuthenticated { get; set; }
    
    bool IsFaceAuthenticated { get;  }
    
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
    /// Authenticates the user with the given credentials.
    /// </summary>
    /// <param name="user"></param>
    /// <param name="password"></param>
    /// <returns></returns>
    public Task<int> DoServerAuthenticationAsync(string user, string password);
    
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
    ///  Gets the information about the authenticated user from the server.
    /// </summary>
    /// <returns></returns>
    public Task<int> GetAuthenticatedUserInfoAsync();
    
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
    
    /// <summary>
    /// Checks if the SAML authentication process is successful
    /// </summary>
    /// <param name="requestId"></param>
    /// <returns></returns>
    public Task<bool> CheckSamlAuthenticationAsync(string requestId);
    
    /// <summary>
    /// Refreshes the authentication token
    /// </summary>
    /// <returns></returns>
    int RefreshToken();


    /// <summary>
    /// Discards the authentication token
    /// </summary>
    void DiscardAuthenticationToken();

    /// <summary>
    /// Notifies the authentication service that the authentication has succeeded.
    /// </summary>
    public void NotifyAuthenticationSucceeded();


    /// <summary>
    /// Registers a face authentication token asynchronously for user authentication.
    /// </summary>
    /// <param name="token">The face authentication token to be registered.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task RegisterFaceAuthenticationTokenAsync(FaceToken token);

    /// <summary>
    /// Retrieves the face authentication token associated with the user.
    /// </summary>
    /// <returns>A face token object containing authentication information.</returns>
    public FaceToken? GetFaceToken();

    event EventHandler? AuthenticationSucceeded;
}