using System;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Exceptions;
using GUIClient.Notifications;
using GUIClient.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using ReactiveUI;
using Serilog;
using ILogger = Serilog.ILogger;

namespace GUIClient.ViewModels
{
    public class ViewModelBase : ReactiveObject, IDisposable, IValidatableViewModel
    {
        private static IStringLocalizer _localizer =  GetService<ILocalizationService>().GetLocalizer(typeof(ViewModelBase).Assembly);
        private IAuthenticationService _authenticationService;
        public static IStringLocalizer Localizer
        {
            get => _localizer;
            set => _localizer = value;
        }

        /// <summary>
        /// Holds whatever validation rules this view-model declares. Views bind
        /// <c>ValidationContext.Text</c> / <c>ValidationContext.IsValid</c> to explain a
        /// blocked Save instead of leaving the user with an unexplained grey button (IX-4).
        /// </summary>
        public ValidationContext ValidationContext { get; } = new();

        /// <summary>
        /// The app's transient-feedback channel (IX-4) — named "Toasts" to keep it distinct from
        /// the persisted in-app notification list in <c>NotificationsViewModel</c>. Report routine
        /// successes here rather than with a modal MessageBox: the user should not have to dismiss
        /// a box to confirm that a save they just asked for worked.
        /// </summary>
        protected static INotificationService Toasts => GetService<INotificationService>();

        private bool _isBusy;

        /// <summary>
        /// True while a long operation is running. IX-4 requires visible busy indication for
        /// anything over ~300 ms, so views bind a <c>ProgressRing</c> to this rather than each
        /// view-model inventing its own flag (several had none at all).
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        /// <summary>
        /// Runs <paramref name="operation"/> with <see cref="IsBusy"/> set, clearing it even if the
        /// operation throws. Nested calls are safe: the flag is only cleared by the outermost scope.
        /// </summary>
        protected async Task WithBusyAsync(Func<Task> operation)
        {
            var wasBusy = IsBusy;
            IsBusy = true;
            try
            {
                await operation();
            }
            finally
            {
                if (!wasBusy) IsBusy = false;
            }
        }

        public string StrSave => Localizer["Save"];
        public string StrCancel => Localizer["Cancel"];
        public string StrOk => Localizer["Ok"];
        public string StrClose => Localizer["Close"];
        /// <summary>Column headers of AvaloniaExtraControls' MultiSelect, whose own defaults are English literals.</summary>
        public string StrAvailable => Localizer["Available"];
        public string StrSelected => Localizer["Selected"];
        public string StrFaceId => "Face ID";
        
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
            ValidationContext.Dispose();
        }
    }
    
    
}
