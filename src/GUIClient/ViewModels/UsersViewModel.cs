using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Authentication;
using Model.DTO;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;

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
    private string StrEnabled { get; }
    private string StrLocked { get; }
    private string StrAdmin { get; }
    private string StrFlags { get; }
    private string StrEmail { get; }
    private string StrRole { get; }
    private string StrManager { get; }
    private string StrInformations { get; }
    private string StrLastLogin { get; }
    private string StrLastPasswordChange { get; }
    
    private string StrSave { get; }
    
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
            SelectedRole = Roles?.Find(x => x.Value == User.RoleId);
            SelectedManager = Users.ToList().Find(x => x.Id == User.Manager);
            Name = User.Name;
            _originalUserName = User.UserName;
            Username = User.UserName;
            
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

    private AuthenticationMethod? _selectedAuthenticationMethod;
    public AuthenticationMethod? SelectedAuthenticationMethod
    {
        get => _selectedAuthenticationMethod;
        set => this.RaiseAndSetIfChanged(ref _selectedAuthenticationMethod, value);
    }
    
    private List<Role>? _roles;
    public List<Role>? Roles
    {
        get => _roles;
        set => this.RaiseAndSetIfChanged(ref _roles, value);
    }
    
    private string? _name;
    public string? Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    private string? _originalUserName;
    private string? _username;
    public string? Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }
    
    private Role? _selectedRole;
    public Role? SelectedRole
    {
        get => _selectedRole;
        set => this.RaiseAndSetIfChanged(ref _selectedRole, value);
    }
    
    private UserListing? _selectedManager;
    public UserListing? SelectedManager
    {
        get => _selectedManager;
        set => this.RaiseAndSetIfChanged(ref _selectedManager, value);
    }
    

    #endregion

    #region PRIVATE FIELDS
        private readonly IUsersService _usersService = GetService<IUsersService>();
        //private readonly IAuthenticationService _authenticationService = GetService<IAuthenticationService>();
        private readonly IRolesService _rolesService = GetService<IRolesService>();
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
        StrEnabled = Localizer["Enabled"];
        StrLocked = Localizer["Locked"];
        StrAdmin = Localizer["Admin"];
        StrFlags = Localizer["Flags"];
        StrEmail = Localizer["Email"];
        StrRole = Localizer["Role"];
        StrManager = Localizer["Manager"];
        StrInformations = Localizer["Informations"];
        StrLastLogin = Localizer["LastLogin"] + ":";
        StrLastPasswordChange = Localizer["LastPasswordChange"] + ":";
        StrSave = Localizer["Save"];

        _users = new ObservableCollection<UserListing>();
        _usersService.UserAdded += (_, user) => _users.Add(user.User!);        
        AuthenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
        };
        
        this.ValidationRule(
            viewModel => viewModel.SelectedRole, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);
        this.ValidationRule(
            viewModel => viewModel.SelectedAuthenticationMethod, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.Name, 
            name => !string.IsNullOrWhiteSpace(name),
            Localizer["PleaseEnterAValueMSG"]);
        
        
        IObservable<bool> usernameUnique =
            this.WhenAnyValue(
                x => x.Username,
                (username) =>
                {
                    if ( _originalUserName == username) return true;
                    var found = Users.ToList().Find(x => x.Username == username);
                    return found == null;
                });
        
        this.ValidationRule(
            viewModel => viewModel.Username,
            usernameUnique,
            Localizer["UsernameMustBeUniqueMSG"]);

    }

    private void Initialize()
    {
        if (_initialized) return;
        Users = new ObservableCollection<UserListing>(_usersService.ListUsers());
        AuthenticationMethods = AuthenticationService.GetAuthenticationMethods();
        Roles = _rolesService.GetAllRoles();
        _initialized = true;
    }
}