using System;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GUIClient.Views;
using ClientServices.Interfaces;
using Splat;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.Http;
using Model.Statistics;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;

namespace GUIClient
{
    public partial class App : Application
    {

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
                        .HasMap<RisksOnDay>((risks, point) =>
                        {
                            point.Coordinate =   new Coordinate(risks.RisksCreated, risks.Day.Day);
                           
                        })
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
                     
                     if(VerifyServerUrl(loadConfigurationWindow.ServerUrl) == false)
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
                }
            }
            
            //Environment.Exit(0);

           
            base.OnFrameworkInitializationCompleted();
        }
        private static T GetService<T>()
        {
            var result = Locator.Current.GetService<T>();
            if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
            return result;
        }

        private bool VerifyServerUrl(string url)
        {
            var result = false;
            try
            {
                var httpClientHandler = new HttpClientHandler();
                httpClientHandler.ServerCertificateCustomValidationCallback =
                    (message, cert, chain, sslPolicyErrors) => true;

                var httpClient = new HttpClient(httpClientHandler);
                var response = httpClient.GetStringAsync(url + "/System/Ping").Result;

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