using Microsoft.Extensions.Localization;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class LocalizableService: ServiceBase
{
    public IStringLocalizer Localizer { get; }
    
    protected LocalizableService(ILogger logger, DALService dalService, IStringLocalizer localizer) : base(logger, dalService)
    {
        Localizer = localizer;
    }
}