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
    /// Get a reliable rest client — one that retries a request the server answered 500, 502, 503 or
    /// 504.
    ///
    /// <b>Reads only.</b> The retry is not a delayed, bounded one: <c>ReliableRestClientWrapper</c>
    /// loops up to eleven times with no delay between attempts, swallowing each exception, and the
    /// Polly policy around it can retry that whole sequence. On a GET that is harmless. On a POST,
    /// PUT, PATCH or DELETE it is a dozen copies of a request that already changed something — the
    /// server may well have committed the row before failing to say so, which is exactly why the
    /// ambiguity cannot be resolved by asking again. Use
    /// <c>RestServiceBase.MutatingClient</c> for those.
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