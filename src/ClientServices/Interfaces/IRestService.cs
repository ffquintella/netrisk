using RestSharp;
using RestSharp.Authenticators;

namespace ClientServices.Interfaces;

public interface IRestService
{
    RestClient GetClient(IAuthenticator? autenticator = null,  bool ignoreTimeVerification = false);
}