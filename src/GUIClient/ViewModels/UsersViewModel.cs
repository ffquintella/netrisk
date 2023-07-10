using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Authentication;
using Model.DTO;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class UsersViewModel: ViewModelBase
{
    #region LANGUAGE
    private string StrUsers { get;  }
    private string StrDetails { get;  }
    private string StrProfiles { get;  }
    private string StrName { get;  }
    private string StrUserName { get; }
    private string StrType { get; }
    
    #endregion

    #region PROPERTIES

    private ObservableCollection<UserListing> _users;
    public ObservableCollection<UserListing> Users
    {
        get => _users;
        set => this.RaiseAndSetIfChanged(ref _users, value);
    }

    private UserListing? _selectedUser;
    public UserListing? SelectedUser
    {
        get => _selectedUser;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedUser, value);
            
            User = _usersService.GetUser(value!.Id);
            SelectedAuthenticationMethod = AuthenticationMethods.ToList()
                .Find(x => x.Name!.ToLower() == User.Type.ToLower());
            
        }
    }

    private string? _selectedUsername = "";
    public string? SelectedUsername
    {
        get => _selectedUsername;
        set => this.RaiseAndSetIfChanged(ref _selectedUsername, value);
    }
    
    private UserDto? _user;
    public UserDto? User
    {
        get => _user;
        set
        {
            this.RaiseAndSetIfChanged(ref _user, value);
        }
    }

    private List<AuthenticationMethod> _authenticationMethods;
    public List<AuthenticationMethod> AuthenticationMethods
    {
        get => _authenticationMethods;
        set => this.RaiseAndSetIfChanged(ref _authenticationMethods, value);
    }
    
    //public ObservableCollection<AuthenticationMethod> AuthenticationMethods =>  
    //    new ObservableCollection<AuthenticationMethod>(AuthenticationService.GetAuthenticationMethods());
    
    private AuthenticationMethod? _selectedAuthenticationMethod;
    public AuthenticationMethod? SelectedAuthenticationMethod
    {
        get => _selectedAuthenticationMethod;
        set => this.RaiseAndSetIfChanged(ref _selectedAuthenticationMethod, value);
    }

    #endregion

    #region PRIVATE FIELDS
        private readonly IUsersService _usersService = GetService<IUsersService>();
        private readonly IAuthenticationService _authenticationService = GetService<IAuthenticationService>();
        private bool _initialized;

    #endregion
    public UsersViewModel()
    {
        StrUsers = Localizer["Users"];
        StrProfiles = Localizer["Profiles"];
        StrDetails = Localizer["Details"];
        StrName = Localizer["Name"];
        StrUserName = Localizer["Username"];
        StrType = Localizer["Type"];

        _users = new ObservableCollection<UserListing>();
        _usersService.UserAdded += (_, user) => _users.Add(user.User!);        
        _authenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
            
        };
        AuthenticationMethods = AuthenticationService.GetAuthenticationMethods();
    }

    private void Initialize()
    {
        if (_initialized) return;
        Users = new ObservableCollection<UserListing>(_usersService.ListUsers());
        _initialized = true;
    }
}