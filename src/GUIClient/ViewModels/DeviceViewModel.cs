using System.Collections.Generic;
using System.Reactive;
using ClientServices.Interfaces;
using MessageBox.Avalonia.DTO;
using MessageBox.Avalonia.Enums;
using Model;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class DeviceViewModel: ViewModelBase
{

    #region LANGUAGES

    private string StrName { get;  }
    private string StrComputer { get;  }
    private string StrLoggedAccount { get;  }
    private string StrActions { get;  }

    #endregion

    #region PROPERTIES

    private List<Client> _clients;
    public List<Client> Clients
    {
        get => _clients;
        set => this.RaiseAndSetIfChanged(ref _clients, value);
    }
    public ReactiveCommand<int, Unit> BtApproveClicked { get; }
    public ReactiveCommand<int, Unit> BtRejectClicked { get; }
    public ReactiveCommand<int, Unit> BtDeleteClicked { get; }

    #endregion

    #region PRIVATE FIELDS
    
    private bool _initialized = false;
    private readonly IAuthenticationService _authenticationService = GetService<IAuthenticationService>();
    private IClientService _clientService;
    
    #endregion
    

    public DeviceViewModel()
    {
        var clientService = GetService<IClientService>();
        _clientService = clientService;
        _clients = new List<Client>();

        StrName = Localizer["Name"];
        StrComputer = Localizer["Computer"];
        StrLoggedAccount= Localizer["LoggedAccount"];
        StrActions= Localizer["Actions"];
        
        BtApproveClicked = ReactiveCommand.Create<int>(ExecuteAproveOrder);
        BtRejectClicked = ReactiveCommand.Create<int>(ExecuteRejectOrder);
        BtDeleteClicked = ReactiveCommand.Create<int>(ExecuteDeleteOrder);

        _authenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
        };
    }

    private void ExecuteAproveOrder(int id)
    {
        var result = _clientService.Approve(id);
        if (result != 0)
        {
            var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["ClientApproveErrorMSG"]  ,
                    Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                });
                        
            messageBoxStandardWindow.Show(); 
        }
        else
        {
            Clients = _clientService.GetAll();
        }
    }
    
    private void ExecuteRejectOrder(int id)
    {
        var result = _clientService.Reject(id);
        if (result != 0)
        {
            var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["ClientRejectErrorMSG"]  ,
                    Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                });
                        
            messageBoxStandardWindow.Show(); 
        }
        else
        {
            Clients = _clientService.GetAll();
        }
    }

    private async void  ExecuteDeleteOrder(int id)
    {
        
        var messageBoxConfirm = MessageBox.Avalonia.MessageBoxManager
            .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["ClientDeleteConfirmationMSG"]  ,
                ButtonDefinitions = ButtonEnum.OkAbort,
                Icon = Icon.Question,
            });
                        
        var confirmation = await messageBoxConfirm.Show();

        if (confirmation == ButtonResult.Ok)
        {
            
            var result = _clientService.Delete(id);
            if (result != 0)
            {
                var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager
                    .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["ClientRejectErrorMSG"]  ,
                        Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                    });
                        
                await messageBoxStandardWindow.Show(); 
            }
            else
            {
                Clients = _clientService.GetAll();
            }
        }
         
    }

    private void Initialize()
    {
        if (!_initialized)
        {
            Clients = _clientService.GetAll();
            _initialized = true;
        }
    }
    
}