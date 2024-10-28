using ClientServices.Interfaces;
using Splat;

namespace ClientServices.Services;
using Serilog;

public class RestServiceBase(IRestService restService) : ServiceBase
{
    protected IRestService RestService { get; } = restService;
}