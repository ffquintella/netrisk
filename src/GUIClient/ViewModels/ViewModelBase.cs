using System;
using ClientServices.Interfaces;
using GUIClient.Exceptions;
using Microsoft.Extensions.Localization;
using ReactiveUI.Validation.Helpers;
using Serilog;
using Splat;
using ILogger = Serilog.ILogger;

namespace GUIClient.ViewModels
{
    public class ViewModelBase : ReactiveValidationObject
    {
        protected IStringLocalizer _localizer;
        private IAuthenticationService _authenticationService;
        public IStringLocalizer Localizer
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
            var localizationService = GetService<ILocalizationService>();
            _authenticationService = GetService<IAuthenticationService>();
            _logger = Log.Logger;
            var localizer = localizationService.GetLocalizer(typeof(ViewModelBase).Assembly);
            if (localizer == null)
            {
                Logger.Error("Error getting localizer service");
                throw new DIException("Error getting localizer service");
            }

            _localizer = localizer;

        }
        
        protected static T GetService<T>()
        {
            var result = Locator.Current.GetService<T>();
            if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
            return result;
        } 
    }
    
    
}