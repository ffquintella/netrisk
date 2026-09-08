using RestSharp;
using RestSharp.Authenticators;

namespace ClientServices.Interfaces;

public interface IRestService
{
    /// <summary>
    /// Get a rest client (default not reliable)
    /// </summary>
    /// <param name="autenticator"></param>
    /// <param name="ignoreTimeVerification"></param>
    /// <param name="reportErrorResponses">
    /// Return a non-2xx as a response instead of raising it as an opaque
    /// <see cref="System.Net.Http.HttpRequestException"/>, so the caller can read the body the server
    /// sent to explain the refusal.
    /// </param>
    /// <returns></returns>
    RestClient GetClient(IAuthenticator? autenticator = null,  bool ignoreTimeVerification = false,
        bool reportErrorResponses = false);
    
    /// <summary>
    /// Get a reliable rest client
    /// </summary>
    /// <param name="autenticator"></param>
    /// <param name="ignoreTimeVerification"></param>
    /// <param name="reportErrorResponses">
    /// Return a non-2xx as a response instead of raising it as an opaque
    /// <see cref="System.Net.Http.HttpRequestException"/>, so the caller can read the body the server
    /// sent to explain the refusal.
    /// </param>
    /// <returns></returns>
    public IRestClient GetReliableClient(IAuthenticator? autenticator = null, bool ignoreTimeVerification = false,
        bool reportErrorResponses = false);
}