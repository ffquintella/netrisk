using Avalonia;
using ReactiveUI.Avalonia;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Svg.Skia;
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
            if (args.Contains("--environment"))
            {
                var idx = args.IndexOf("--environment");
                var env = args[idx + 1];
                if (string.IsNullOrWhiteSpace(environment))
                {
                    Console.WriteLine("Unkown environment");
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
            GC.KeepAlive(typeof(SvgImageExtension).Assembly);
            GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI()
                .UseSkia();
        }
    }
}
