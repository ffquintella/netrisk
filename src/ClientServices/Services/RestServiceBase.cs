using ClientServices.Interfaces;
using Splat;

namespace ClientServices.Services;
using Serilog;

public class RestServiceBase: ServiceBase
{
    protected IRestService RestService { get; }

    public RestServiceBase(
        IRestService restService): base()
    {
        RestService = restService;

    }
    

}