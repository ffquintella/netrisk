using Aura.UI.FluentTheme;
using Avalonia;
using ReactiveUI.Avalonia;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading;
using Avalonia.Controls;
using ClientServices;
using ClientServices.Interfaces;
using DynamicData;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace GUIClient
{
    class Program
    {
        private const int TimeoutSeconds = 3;

        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            string environment = "production";
            var envArg = args.FirstOrDefault(a => a.StartsWith("--environment=", StringComparison.Ordinal));
            if (envArg != null)
            {
                // --environment=dev
                var env = envArg.Substring("--environment=".Length);
                if (string.IsNullOrWhiteSpace(env))
                {
                    Console.WriteLine("Unknown environment");
                    return;
                }
                environment = env;
            }
            else if (args.Contains("--environment"))
            {
                // --environment dev
                var idx = args.IndexOf("--environment");
                var env = idx + 1 < args.Length ? args[idx + 1] : null;
                if (string.IsNullOrWhiteSpace(env))
                {
                    Console.WriteLine("Unknown environment");
                    return;
                }
                environment = env;
            }

            SubscribeToDomainUnhandledEvents();
            RegisterDependencies(environment);

            if (args.Contains("--cleanServer"))
            {
                var mutableConfigurationService = ServiceProvider.GetRequiredService<IMutableConfigurationService>();
                mutableConfigurationService.RemoveConfigurationValue("Server");
            }

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        }

        private static void SubscribeToDomainUnhandledEvents() =>
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var logger = ServiceProvider.GetService<ILogger>();
                if (logger == null)
                {
                    Console.WriteLine("Unhandled application error: {0}", args.ExceptionObject);
                    return;
                }
                var ex = (Exception)args.ExceptionObject;
                logger.LogCritical($"Unhandled application error: {ex}");
            };

        private static void RegisterDependencies(string environment)
        {
            var services = new ServiceCollection();
            Bootstrapper.Register(services, environment);
            ServiceProvider = services.BuildServiceProvider();
            ServiceProviderAccessor.Provider = ServiceProvider;
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        private static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI(_ => { })
                .UseSkia()
                .UseAuraUIFluentTheme();
        }
    }
}
