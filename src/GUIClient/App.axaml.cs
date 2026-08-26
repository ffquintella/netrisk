using System;
using System.Net.Http;
using System.Security.Authentication;
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
using Model.Configuration;
using Model.Statistics;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Tools.Security;
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
                 // Size is declared in XAML only (IX-1) — the launcher no longer overrides it.
                 var loadConfigurationWindow = new LoadConfigurationWindow();
                 loadConfigurationWindow.Show();
                 
                 loadConfigurationWindow.Closed += async (sender, args) =>
                 {
                     
                     if(loadConfigurationWindow.ServerUrl == "")
                     {
                         Environment.Exit(0);
                     }
                     
                     // Both sources, through the same resolution RestService uses. Reading only the
                     // persisted store here would make the setting the error message below tells the
                     // operator to set do nothing — and since this check gates whether the server URL
                     // is ever saved, a client facing a self-signed server could then never be
                     // configured at all.
                     var allowInvalidCertificate = ServerCertificatePolicy.Resolve(
                         mutableConfigurationService.GetConfigurationValue("AllowInvalidCertificate"),
                         GetService<ServerConfiguration>().AllowInvalidCertificate);

                     var verificationError = await VerifyServerUrlAsync(
                         loadConfigurationWindow.ServerUrl, allowInvalidCertificate);

                     if(verificationError != null)
                     {
                         var msgError = MessageBoxManager.GetMessageBoxStandard(
                             new MessageBoxStandardParams
                             {
                                 ContentTitle = "ERRO",
                                 ContentMessage = verificationError,
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
                    // Size is declared in MainWindow.axaml and then restored from the persisted
                    // geometry (IX-1/IX-7); the launcher no longer forces it.
                    desktop.MainWindow = new MainWindow();

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

        /// <summary>
        /// Pings a candidate server URL during first-run configuration.
        ///
        /// Track 7 finding NR-2026-005. This used to accept any certificate unconditionally, which
        /// meant the very step that decides which server the client will trust for the rest of its
        /// life was itself unauthenticated. It now validates by default, honours the same explicit
        /// per-installation opt-in as every other client call
        /// (<see cref="ServerCertificatePolicy"/>), and — as milestone 7.4.1 requires — reports a
        /// certificate failure as a certificate failure instead of folding it into "invalid URL".
        /// </summary>
        /// <returns>Null on success, or the message to show the user.</returns>
        private static async Task<string?> VerifyServerUrlAsync(string url, bool allowInvalidCertificate)
        {
            var handler = new HttpClientHandler();

            var callback = ServerCertificatePolicy.CreateCallback(
                allowInvalidCertificate, message => Log.Warning("{Message}", message));

            if (callback != null)
                handler.ServerCertificateCustomValidationCallback =
                    (_, certificate, chain, errors) => callback(handler, certificate, chain, errors);

            try
            {
                using var httpClient = new HttpClient(handler);
                var response = await httpClient.GetStringAsync(url + "/System/Ping");

                return response == "Pong" ? null : "That address answered, but it is not a NetRisk server.";
            }
            catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
            {
                // Deliberately fatal and deliberately specific: silently proceeding over a
                // certificate we could not verify is the outcome this finding was about.
                Log.Error(ex, "TLS validation failed for {Url}", url);

                return "The server's TLS certificate could not be validated, so the connection was "
                       + "refused. Install the server's certificate authority in this machine's trust "
                       + "store, or set " + ServerCertificatePolicy.ConfigurationKey
                       + " if you accept the risk.";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not reach {Url}", url);
                return "Please enter a valid URL";
            }
        }
    }
}
