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
    public ReactiveCommand<int, Unit> BtDeleteClicked { get; }
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

    private async void ExecuteRead(int messageId)
    {
        await _messagesService.ReadMessageAsync(messageId);
        Log.Information("Marking message as read: {MessageId}", messageId);
        await InitializeAsync();
    }
    
    private async void ExecuteDelete(int messageId)
    {
        await _messagesService.DeleteMessageAsync(messageId);
        Log.Information("Deleting message: {MessageId}", messageId);
        await InitializeAsync();
    }
    
    

    #endregion
}