using ClientServices.Interfaces;

namespace ClientServices.Services;

public class SystemService: ServiceBase, ISystemService
{
    public SystemService(IRestService restService) : base(restService)
    {
    }

    public bool NeedsUpgrade()
    {

        return true;
    }
}