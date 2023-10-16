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
    /// <returns></returns>
    RestClient GetClient(IAuthenticator? autenticator = null,  bool ignoreTimeVerification = false);
    
    /// <summary>
    /// Get a reliable rest client
    /// </summary>
    /// <param name="autenticator"></param>
    /// <param name="ignoreTimeVerification"></param>
    /// <returns></returns>
    public IRestClient GetReliableClient(IAuthenticator? autenticator = null, bool ignoreTimeVerification = false);
}