using Mapster;
using DAL.Entities;
using DAL.EntitiesDto;
using Microsoft.Extensions.Logging;
using Model.DTO;
using Serilog;
using Splat;
using ILogger = Splat.ILogger;

namespace GUIClient;

public class MapperBootstrapper: BaseBootstrapper
{
    public static void RegisterServices(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver)
    {
        
        var iLoggerFactory = resolver.GetService<ILoggerFactory>();
        
        // Mapster does not require explicit configuration for simple mappings.
        // If custom mappings are needed, use TypeAdapterConfig.
        // Remove AutoMapper registration.
        
    }
}