using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class SettingsService(ILogger logger, IDalService dalService):  ServiceBase(logger, dalService), ISettingsService
{
    
}