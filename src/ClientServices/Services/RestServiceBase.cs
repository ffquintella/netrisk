using ClientServices.Interfaces;

namespace ClientServices.Services;

public class RestServiceBase(IRestService restService) : ServiceBase
{
    protected IRestService RestService { get; } = restService;
}
