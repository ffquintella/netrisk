using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Messages;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using Serilog;

namespace GUIClient.ViewModels;

public class NotificationsViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrNotifications { get; set; } = Localizer["Notifications"];

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
    public ReactiveCommand<int, RxVoid> BtReadClicked { get; }
    public ReactiveCommand<int, RxVoid> BtDeleteClicked { get; }
    #endregion
    
    #region CONSTRUCTOR
    public NotificationsViewModel()
    {

        _= InitializeAsync();

        
        BtReadClicked = ReactiveCommand.Create<int>(ExecuteRead);
        BtDeleteClicked = ReactiveCommand.Create<int>(ExecuteDelete);
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

    // Both handlers are `async void` command bodies, so an exception escaping them is unhandled
    // rather than surfaced. Now that the messages service reports a rejected read or delete instead
    // of swallowing it, the failure has to be caught and logged here; the list is reloaded either
    // way so the view never keeps showing state the server did not accept.
    private async void ExecuteRead(int messageId)
    {
        try
        {
            await _messagesService.ReadMessageAsync(messageId);
            Log.Information("Marking message as read: {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            Log.Error("Error marking message {MessageId} as read: {Message}", messageId, ex.Message);
        }

        await InitializeAsync();
    }
    
    private async void ExecuteDelete(int messageId)
    {
        try
        {
            await _messagesService.DeleteMessageAsync(messageId);
            Log.Information("Deleting message: {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            Log.Error("Error deleting message {MessageId}: {Message}", messageId, ex.Message);
        }

        await InitializeAsync();
    }
    
    

    #endregion
}