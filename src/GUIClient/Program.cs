﻿using Avalonia;
using Avalonia.ReactiveUI;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Svg.Skia;
using ClientServices.Interfaces;
using Splat;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace GUIClient
{
    class Program
    {
        private const int TimeoutSeconds = 3;
        
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
        
            var mutex = new Mutex(false, typeof(Program).FullName);

            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(TimeoutSeconds), true))
                {
                    return;
                }

                SubscribeToDomainUnhandledEvents();
                RegisterDependencies();

                if (args.Contains("--cleanServer"))
                {
                    var mutableConfigurationService = Locator.Current.GetService<IMutableConfigurationService>();
                    mutableConfigurationService!.RemoveConfigurationValue("Server");
                }

                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
            }
            finally
            {
                mutex.ReleaseMutex();
            }
            
        } 

        private static void SubscribeToDomainUnhandledEvents() =>
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var logger = Locator.Current.GetService<ILogger>();
                if (logger == null) throw new Exception("Could not load logger");
                var ex = (Exception) args.ExceptionObject;

                logger.LogCritical($"Unhandled application error: {ex}");
            };
        
        private static void RegisterDependencies() =>
            Bootstrapper.Register(Locator.CurrentMutable, Locator.Current);
        
        // Avalonia configuration, don't remove; also used by visual designer.
        private static AppBuilder BuildAvaloniaApp()
        {
            GC.KeepAlive(typeof(SvgImageExtension).Assembly);
            GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();
        }
            
    }
}