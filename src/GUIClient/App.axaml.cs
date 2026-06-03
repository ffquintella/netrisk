using System;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GUIClient.Helpers;
using GUIClient.Views;
using ClientServices.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.Http;
using Model.Statistics;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
#if DEBUG
using InProcess.DevTools;
#endif

namespace GUIClient
{
    public partial class App : Application
    {
        private TrayIconManager? _trayIconManager;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            
            LiveCharts.Configure(config => 
                    config 
                        // registers SkiaSharp as the library backend
                        // REQUIRED unless you build your own
                        .AddSkiaSharp() 
                        
                        // adds the default supported types
                        // OPTIONAL but highly recommend
                        .AddDefaultMappers() 

                        // select a theme, default is Light
                        // OPTIONAL
                        .AddDarkTheme()
                        //.AddLightTheme() 

                        // finally register your own mappers
                        // you can learn more about mappers at:
                        /*.HasMap<RisksOnDay>((risks, point) =>
                        {
                            
                            point.Coordinate =   new Coordinate(risks.RisksCreated, risks.Day.Day);
                           
                        })*/
            );
            
            
        }

        public  override void OnFrameworkInitializationCompleted()
        {
            
            var mutableConfigurationService = GetService<IMutableConfigurationService>();
            mutableConfigurationService.Initialize();
            
            var server = mutableConfigurationService.GetConfigurationValue("Server");
            
            //Server not configured yet
            if (server == null)
            {
                 var loadConfigurationWindow = new LoadConfigurationWindow();
                 loadConfigurationWindow.Width = 400;
                 loadConfigurationWindow.Height = 180;
                 loadConfigurationWindow.Show();
                 
                 loadConfigurationWindow.Closed += async (sender, args) =>
                 {
                     
                     if(loadConfigurationWindow.ServerUrl == "")
                     {
                         Environment.Exit(0);
                     }
                     
                     if(await VerifyServerUrlAsync(loadConfigurationWindow.ServerUrl) == false)
                     {
                         var msgError = MessageBoxManager.GetMessageBoxStandard(
                             new MessageBoxStandardParams
                             {
                                 ContentTitle = "ERRO",
                                 ContentMessage = "Please enter a valid URL",
                                 Icon = MsBox.Avalonia.Enums.Icon.Error,
                                 WindowStartupLocation = WindowStartupLocation.CenterOwner
                             });

                         await msgError.ShowAsync();
                         Environment.Exit(0);
                     }else
                     {
                         mutableConfigurationService.SetConfigurationValue("Server", loadConfigurationWindow.ServerUrl);

                         var msgError = MessageBoxManager.GetMessageBoxStandard(
                             new MessageBoxStandardParams
                             {
                                 ContentTitle = "INFO",
                                 ContentMessage = "Please restart the application",
                                 Icon = MsBox.Avalonia.Enums.Icon.Info,
                                 WindowStartupLocation = WindowStartupLocation.CenterOwner
                             });

                         await msgError.ShowAsync();
                         
                         Environment.Exit(0);
                     }
                     
                 };
                 
            }
            else
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow = new MainWindow();
                    desktop.MainWindow.Width = 1100;
                    desktop.MainWindow.Height = 900;

                    // System-tray integration (Windows tray / macOS menu-bar extra) with
                    // a quick-status preview and minimise-to-tray on Windows.
                    _trayIconManager = new TrayIconManager(this, desktop.MainWindow);
                    _trayIconManager.Initialize();
                    desktop.Exit += (_, _) => _trayIconManager?.Dispose();
                }
            }
            
            //Environment.Exit(0);

           
            base.OnFrameworkInitializationCompleted();

#if DEBUG
            this.AttachDevTools(new DevToolsOptions()
            {
                EnableMcpServer = true,
                McpServer = new McpServerOptions()
                {
                    Host = "127.0.0.1",
                    Port = 43210,
                    Path = "/mcp",
                    EnableDomInspection = true,
                    EnableScreenshots = true,
                    EnableNavigation = true,
                    EnableEvents = true,
                    EnableStateMutation = true
                }
            });
#endif
        }
        private static T GetService<T>()
        {
            var result = Program.ServiceProvider.GetService<T>();
            if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
            return result;
        }

        private async Task<bool> VerifyServerUrlAsync(string url)
        {
            var result = false;
            try
            {
                var httpClientHandler = new HttpClientHandler();
                httpClientHandler.ServerCertificateCustomValidationCallback =
                    (message, cert, chain, sslPolicyErrors) => true;

                var httpClient = new HttpClient(httpClientHandler);
                var response = await httpClient.GetStringAsync(url + "/System/Ping");

                if (response == "Pong")
                {
                    result = true;
                }

            }
            catch
            {
                result = false;
            }

            return result;
        }
    }
}
