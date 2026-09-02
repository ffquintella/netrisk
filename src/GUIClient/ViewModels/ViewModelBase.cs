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

        /// <summary>
        /// Runs a write with busy indication, a success toast, and the server's own message on
        /// failure.
        ///
        /// The last part is the reason this exists rather than each view-model writing its own
        /// try/catch: the Track 8 endpoints answer a refusal with a sentence written to be read by a
        /// person — "Residual 9.10 is above the acceptance ceiling of 6.00", "You cannot accept this
        /// risk because you own it" — and a handler that replaced it with "the operation failed" would
        /// turn a refusal the user can act on into one they cannot.
        /// </summary>
        protected async Task RunAsync(string successMessage, Func<Task> operation)
        {
            try
            {
                await WithBusyAsync(operation);
                if (!string.IsNullOrWhiteSpace(successMessage)) Toasts.Success(successMessage);
            }
            catch (Model.Exceptions.InvalidHttpRequestException ex)
            {
                Logger.Warning("Refused: {Message}", ex.Message);
                Toasts.Error(Explain(ex.Message));
            }
            catch (Model.Exceptions.DataNotFoundException ex)
            {
                Logger.Warning("Not found: {Message}", ex.Message);
                Toasts.Error(Localizer["ItemNotFoundMSG"]);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unexpected failure during a write");
                Toasts.Error(Localizer["ErrorSavingMSG"]);
            }
        }

        /// <summary>
        /// Pulls the human-readable <c>message</c> out of the API's structured error body, falling
        /// back to the raw text. Without this the user is shown a JSON object.
        /// </summary>
        private static string Explain(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return Localizer["ErrorSavingMSG"];

            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(body);

                if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    foreach (var name in new[] { "message", "Message" })
                        if (document.RootElement.TryGetProperty(name, out var property) &&
                            property.ValueKind == System.Text.Json.JsonValueKind.String)
                            return property.GetString() ?? body;
            }
            catch (System.Text.Json.JsonException)
            {
                // Not JSON — the raw text is still better than a generic message, as long as it is
                // short enough to be a message rather than an error page.
            }

            return body.Length <= 400 ? body : Localizer["ErrorSavingMSG"];
        }

        /// <summary>
        /// The message to show for a failed read.
        ///
        /// <see cref="RunAsync"/> covers writes; a live read against a third-party API needs the same
        /// unwrapping — the server's structured error carries the sentence that actually helps ("Assets
        /// needs Jira Service Management Premium"), and showing a generic "loading failed" instead
        /// sends the operator looking in the wrong place.
        /// </summary>
        protected static string ExplainError(Exception ex) =>
            ex is Model.Exceptions.DataNotFoundException
                ? Localizer["ItemNotFoundMSG"]
                : Explain(ex.Message);

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
