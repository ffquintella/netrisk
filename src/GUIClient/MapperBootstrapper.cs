using Microsoft.Extensions.DependencyInjection;

namespace GUIClient;

public class MapperBootstrapper : BaseBootstrapper
{
    public static void RegisterServices(IServiceCollection services)
    {
        // Mapster does not require explicit configuration for simple mappings.
        // If custom mappings are needed, use TypeAdapterConfig.
    }
}
