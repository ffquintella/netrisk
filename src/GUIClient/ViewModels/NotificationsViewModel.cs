using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Messages;
using ReactiveUI;
using Serilog;

namespace GUIClient.ViewModels;

public class NotificationsViewModel: ViewModelBase
{
    #region LANGUAGE
    private string StrNotifications { get; set; } = Localizer["Notifications"];

    #endregion
    
    #region PROPERTIES
        private ObservableCollection<Message> _notifications = new ();
        public ObservableCollection<Message> Notifications
        {
            get => _notifications;
            set => this.RaiseAndSetIfChanged(ref _notifications, value);
        }
    #endregion
    
    #region SERVICES

    private readonly IMessagesService _messagesService = GetService<IMessagesService>();
    
    #endregion
    
    #region BUTTONS
    public ReactiveCommand<int, Unit> BtReadClicked { get; }
    #endregion
    
    #region CONSTRUCTOR
    public NotificationsViewModel()
    {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        InitializeAsync();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        
        BtReadClicked = ReactiveCommand.Create<int>(ExecuteRead);
    }
    #endregion
    
    #region METHODS

    public async Task InitializeAsync()
    {
        var chats = new List<int?>
        {
            (int) ChatTypes.Jobs,
            (int) ChatTypes.GeneralAlerts
        };
        
        Notifications = new ObservableCollection<Message>( await _messagesService.GetMessagesAsync(chats));
    }

    private async void ExecuteRead(int messageId)
    {
        await _messagesService.ReadMessageAsync(messageId);
        Log.Information("Marking message as read: {MessageId}", messageId);
        await InitializeAsync();
    }
    
    

    #endregion
}