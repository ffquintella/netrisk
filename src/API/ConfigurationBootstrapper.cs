using System.Threading.RateLimiting;

namespace API;

public class ConfigurationBootstrapper
{
    public static void RegisterConfiguration(IServiceCollection services, IConfiguration config)
    {

        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 1000,
                        QueueLimit = 150,
                        Window = TimeSpan.FromMinutes(1)
                    }
                )
            );
        });

    }
}

