using System;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using ReactiveUI;
using Serilog;
using ILogger = Serilog.ILogger;

namespace GUIClient.ViewModels
{
    public class ViewModelBase : ReactiveObject, IDisposable
    {
        private static IStringLocalizer _localizer =  GetService<ILocalizationService>().GetLocalizer(typeof(ViewModelBase).Assembly);
        private IAuthenticationService _authenticationService;
        public static IStringLocalizer Localizer
        {
            get => _localizer;
            set => _localizer = value;
        }
        
        private ILogger _logger;
        public ILogger Logger
        {
            get => _logger;
            set => _logger = value;
        }
         
        public IAuthenticationService AuthenticationService
        {
            get => _authenticationService;
            set => _authenticationService = value;
        }
        public ViewModelBase()
        {
            //var localizationService = GetService<ILocalizationService>();
            _authenticationService = GetService<IAuthenticationService>();
            _logger = Log.Logger;
    
            
        }
        

        
        protected static T GetService<T>() where T : notnull
        {
            return Program.ServiceProvider.GetRequiredService<T>();
        } 
        
        protected static async Task<T> GetServiceAsync<T>() where T : notnull
        {
            return await Task.Run(() =>
            {
                return Program.ServiceProvider.GetRequiredService<T>();
            });
        } 

        public virtual void Dispose()
        {
        }
    }
    
    
}
