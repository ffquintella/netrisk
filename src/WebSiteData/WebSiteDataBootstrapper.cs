using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebSiteData.Services;
using WebSiteData.Sync;

namespace WebSiteData;

public static class WebSiteDataBootstrapper
{
    public static void RegisterLocalData(IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<WebSiteDbContext>(o => o.UseSqlite(connectionString));

        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<ILocalFixReportService, LocalFixReportService>();
        services.AddScoped<ILocalUserService, LocalUserService>();
        services.AddScoped<ILocalLinkService, LocalLinkService>();
        services.AddScoped<ILocalIrpService, LocalIrpService>();
        services.AddScoped<ISyncApplyService, SyncApplyService>();
        services.AddSingleton<SyncSignatureVerifier>();
    }

    /// <summary>Creates the local schema if needed and enables WAL for concurrent reads during sync writes.</summary>
    public static void InitializeDatabase(IServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<WebSiteDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
}
