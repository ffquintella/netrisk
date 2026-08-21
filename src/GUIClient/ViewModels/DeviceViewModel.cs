using System.Linq;
using GUIClient.Tools;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

public class DeviceViewModel: ViewModelBase
{
    #region LANGUAGES

    public string StrName { get;  }
    public string StrComputer { get;  }
    public string StrLoggedAccount { get;  }
    public string StrActions { get;  }
    public string StrDevices { get; }
    public string StrApprove { get; }
    public string StrReject { get; }
    public string StrDelete { get; }
    public string StrReload { get; }
    public string StrStatus { get; }

    #endregion

    #region PROPERTIES

    private List<Client> _clients;
    public List<Client> Clients
    {
        get => _clients;
        set => this.RaiseAndSetIfChanged(ref _clients, value);
    }

    private Client? _selectedClient;

    /// <summary>
    /// The row the toolbar acts on. IX-5 archetype B: this was the only view in the app with
    /// per-row action buttons; the actions now live in a toolbar acting on the selection.
    /// </summary>
    public Client? SelectedClient
    {
        get => _selectedClient;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedClient, value);
            ProcessStatusButtons();
        }
    }

    private bool _btApproveEnabled;
    public bool BtApproveEnabled
    {
        get => _btApproveEnabled;
        set => this.RaiseAndSetIfChanged(ref _btApproveEnabled, value);
    }

    private bool _btRejectEnabled;
    public bool BtRejectEnabled
    {
        get => _btRejectEnabled;
        set => this.RaiseAndSetIfChanged(ref _btRejectEnabled, value);
    }

    private bool _btDeleteEnabled;
    public bool BtDeleteEnabled
    {
        get => _btDeleteEnabled;
        set => this.RaiseAndSetIfChanged(ref _btDeleteEnabled, value);
    }

    /// <summary>Status-bar text: the register archetype ends in a count (IX-5 B).</summary>
    public string StatusBarText => string.Format(Localizer["DeviceCountMSG"], Clients?.Count ?? 0);

    public ReactiveCommand<RxVoid, RxVoid> BtApproveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRejectClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }

    #endregion

    #region PRIVATE FIELDS
    
    private bool _initialized = false;
    private readonly IClientService _clientService;
    
    #endregion
    
    #region CONSTRUCTOR
    public DeviceViewModel()
    {
        var clientService = GetService<IClientService>();
        _clientService = clientService;
        _clients = new List<Client>();

        StrName = Localizer["Name"];
        StrComputer = Localizer["Computer"];
        StrLoggedAccount= Localizer["LoggedAccount"];
        StrActions= Localizer["Actions"];
        StrDevices = Localizer["Devices"];
        StrApprove = Localizer["Approve"];
        StrReject = Localizer["Reject"];
        StrDelete = Localizer["Delete"];
        StrReload = Localizer["Reload"];
        StrStatus = Localizer["Status"];
        
        BtApproveClicked = ReactiveCommand.Create(() => ExecuteApproveOrder(SelectedClient!.Id),
            this.WhenAnyValue(x => x.BtApproveEnabled));
        BtRejectClicked = ReactiveCommand.Create(() => ExecuteRejectOrder(SelectedClient!.Id),
            this.WhenAnyValue(x => x.BtRejectEnabled));
        BtDeleteClicked = ReactiveCommand.CreateFromTask(() => ExecuteDeleteOrderAsync(SelectedClient!.Id),
            this.WhenAnyValue(x => x.BtDeleteEnabled));
        BtReloadClicked = ReactiveCommand.Create(Reload);

        AuthenticationService.AuthenticationSucceeded += (_, _) =>
        {
            
            if(AuthenticationService.AuthenticatedUserInfo == null)
                return;
            
            if(AuthenticationService.AuthenticatedUserInfo!.UserRole != "Administrator")
                return;
            
            Initialize();
        };
    }
    #endregion

    #region METHODS

    /// <summary>
    /// Recomputes which device actions apply to the selected row. Mirrors the vulnerability
    /// toolbar's state machine: every action stays visible, enabled per current status (IX-6).
    /// </summary>
    private void ProcessStatusButtons()
    {
        var client = SelectedClient;

        BtApproveEnabled = client?.Status == "requested";
        BtRejectEnabled = client != null && client.Status != "rejected";
        BtDeleteEnabled = client != null;
    }

    private void Reload()
    {
        Clients = _clientService.GetAll();
        SelectedClient = null;
        this.RaisePropertyChanged(nameof(StatusBarText));
    }

    private void ExecuteApproveOrder(int id)
    {
        var result = _clientService.Approve(id);
        if (result != 0)
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["ClientApproveErrorMSG"]  ,
                    Icon = Icon.Warning,
                });
                        
            messageBoxStandardWindow.ShowAsync(); 
        }
        else
        {
            Reload();
        }
    }
    
    private void ExecuteRejectOrder(int id)
    {
        var result = _clientService.Reject(id);
        if (result != 0)
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["ClientRejectErrorMSG"]  ,
                    Icon = Icon.Warning,
                });
                        
            messageBoxStandardWindow.ShowAsync(); 
        }
        else
        {
            Reload();
        }
    }

    private async Task ExecuteDeleteOrderAsync(int id)
    {
        
        var device = Clients?.FirstOrDefault(c => c.Id == id);

        if (await ConfirmationDialog.ConfirmDeleteAsync(device?.Name ?? $"#{id}",
                Localizer["ClientDeleteConfirmationMSG"]))
        {
            
            var result = _clientService.Delete(id);
            if (result != 0)
            {
                var messageBoxStandardWindow = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["ClientRejectErrorMSG"]  ,
                        Icon = Icon.Warning,
                    });
                        
                await messageBoxStandardWindow.ShowAsync(); 
            }
            else
            {
                Reload();
            }
        }
         
    }

    public void Initialize()
    {
        if (!_initialized)
        {
            Task.Run(() =>
            {
                Reload();
            });
            _initialized = true;
        }
    }
    
    #endregion
}