using Microsoft.Extensions.Localization;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class LocalizableService: ServiceBase
{
    private ILocalizationService Localization { get; }
    
    protected LocalizableService(ILogger logger, DALService dalService, ILocalizationService localization) : base(logger, dalService)
    {
        Localization = localization;
    }
    
    protected IStringLocalizer Localizer => Localization.GetLocalizer();
}