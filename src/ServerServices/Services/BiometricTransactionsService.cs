using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class BiometricTransactionsService: ServiceBase, IBiometricTransactionsService
{
    protected BiometricTransactionsService(ILogger logger, IDalService dalService) : base(logger, dalService)
    {
    }
    
    
}