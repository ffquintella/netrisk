using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using AvaloniaExtraControls.Models;
using ClientServices.Interfaces;
using ClientServices.Services;
using DAL.Entities;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using GUIClient.Views;
using Model.Authentication;
using Model.DTO;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;

namespace GUIClient.ViewModels;

public class UsersViewModel: ViewModelBase
{
    #region LANGUAGE
    private string StrUsers { get;  }
    private string StrTeamMembers { get;  }
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
    private string StrPermissions { get; }
    private string StrSave { get; }
    private string StrSelectAll { get; }
    private string StrCleanAll { get; }
    private string StrTeams { get; } 
    private string StrRolePermissions { get; }
    private string StrChangePassword { get; } = Localizer["ChangePassword"];
    
    
    #endregion

    #region PROPERTIES

    private ObservableCollection<UserListing>? _users;
    public ObservableCollection<UserListing>? Users
    {
        get => _users;
        set => this.RaiseAndSetIfChanged(ref _users, value);
    }
    
    private ObservableCollection<Role>? _profiles;
    public ObservableCollection<Role>? Profiles
    {
        get => _profiles;
        set => this.RaiseAndSetIfChanged(ref _profiles, value);
    }
    
    
    private Role? _selectedProfile;
    public Role? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (value != null)
            {
                var selectedPermissionsKeys = _rolesService.GetRolePermissions(value.Value);

                if (Permissions != null)
                {
                    AvailableProfilePermissions = new ObservableCollection<SelectEntity>(Permissions.Where(p => !selectedPermissionsKeys.Contains(p.Key)).Select(p => new SelectEntity(
                        p.Id.ToString(), p.Name)));
                    SelectedProfilePermissions = new ObservableCollection<SelectEntity>(Permissions.Where(p => selectedPermissionsKeys.Contains(p.Key)).Select(r => new SelectEntity(
                        r.Id.ToString(), r.Name)));
                }

            }
            
            this.RaiseAndSetIfChanged(ref _selectedProfile, value);
        }
    }


    private IEnumerable<SelectEntity>? _availableProfilePermissions;
    public IEnumerable<SelectEntity>? AvailableProfilePermissions
    {
        get => _availableProfilePermissions;
        set => this.RaiseAndSetIfChanged(ref _availableProfilePermissions, value);
    }
    
    private IEnumerable<SelectEntity>? _selectedProfilePermissions;
    public IEnumerable<SelectEntity>? SelectedProfilePermissions
    {
        get => _selectedProfilePermissions;
        set => this.RaiseAndSetIfChanged(ref _selectedProfilePermissions, value);
    }
    
    private ObservableCollection<Team>? _teams;
    public ObservableCollection<Team>? Teams
    {
        get => _teams;
        set => this.RaiseAndSetIfChanged(ref _teams, value);
    }

    private Team? _selectedTeam;
    public Team? SelectedTeam
    {
        get => _selectedTeam;
        set
        {
            if (value != null)
            {
                var selectedUsersIds = TeamsService.GetUsersIds(value.Value);

                if (Users != null)
                {
                    AvailableTeamUsers = new ObservableCollection<SelectEntity>(Users.Where(u => !selectedUsersIds.Contains(u.Id)).Select(u => new SelectEntity(
                        u.Id.ToString(), u.Name)));
                    SelectedTeamUsers = new ObservableCollection<SelectEntity>(Users.Where(u => selectedUsersIds.Contains(u.Id)).Select(u => new SelectEntity(
                        u.Id.ToString(), u.Name)));
                }

            }

            
            this.RaiseAndSetIfChanged(ref _selectedTeam, value);
        }
    }
    
    private IEnumerable<SelectEntity>? _availableTeamUsers;
    public IEnumerable<SelectEntity>? AvailableTeamUsers
    {
        get => _availableTeamUsers;
        set => this.RaiseAndSetIfChanged(ref _availableTeamUsers, value);
    }
    
    private IEnumerable<SelectEntity>? _selectedTeamUsers;
    public IEnumerable<SelectEntity>? SelectedTeamUsers
    {
        get => _selectedTeamUsers;
        set => this.RaiseAndSetIfChanged(ref _selectedTeamUsers, value);
    }

    private bool _changePasswordEnabled;
    
    public bool ChangePasswordEnabled
    {
        get => _changePasswordEnabled;
        set => this.RaiseAndSetIfChanged(ref _changePasswordEnabled, value);
    }
    
    private UserListing? _selectedUser;
    public UserListing? SelectedUser
    {
        get => _selectedUser;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedUser, value);

            try
            {
                if (value!.Id > 0)
                {
                    User = _usersService.GetUser(value.Id);
                    if(AuthenticationMethods != null)
                        SelectedAuthenticationMethod = AuthenticationMethods.ToList()
                            .Find(x => x.Name!.ToLower() == User.Type.ToLower());
                    SelectedRole = Roles?.Find(x => x.Value == User.RoleId);
                    SelectedManager = Users?.ToList().Find(x => x.Id == User.Manager);
                    Name = User.Name;
                    _originalUserName = User.UserName;
                    Username = User.UserName;
                    Email = User.Email;
                    
                    if(SelectedAuthenticationMethod != null && SelectedAuthenticationMethod.Name!.ToLower() == "local")
                        ChangePasswordEnabled = true;
                    else
                        ChangePasswordEnabled = false;

                    PermissionSelection.DeselectRange(0, ((IEnumerable<Permission>)PermissionSelection.Source!).Count());
                    foreach (var permission in _usersService.GetUserPermissions(value.Id))
                    {
                        var index = ((IEnumerable<Permission>)PermissionSelection.Source!).ToList().TakeWhile(t => t.Id != permission.Id).Count();
                        PermissionSelection.Select(index);
                    }
                }
                else
                {
                    ChangePasswordEnabled = false;
                    User = new UserDto()
                    {
                        Id = 0,
                        Name = "",
                        UserName = "",
                        RoleId = 0,
                        Manager = 0,
                        Type = "",
                        Email = "",
                        Enabled = true,
                        ChangePassword = 0,
                        Lockout = false
                    };
                    _originalUserName = "";
                    Name = "";
                    Username = "";
                    Email = "";
                    SelectedManager = null;
                    SelectedRole = null;
                    SelectedAuthenticationMethod = null;
                    PermissionSelection.DeselectRange(0, ((IEnumerable<Permission>)PermissionSelection.Source!).Count());
                    this.RaiseAndSetIfChanged(ref _selectedUser, value);
                }

                
            }catch(Exception e)
            {
                Console.WriteLine(e);
            }

            
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
        private set => this.RaiseAndSetIfChanged(ref _user, value);
    }

    private List<AuthenticationMethod>? _authenticationMethods;
    public List<AuthenticationMethod>? AuthenticationMethods
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

    private ObservableCollection<Permission>? _permissions;
    public ObservableCollection<Permission>? Permissions
    {
        get => _permissions;
        set => this.RaiseAndSetIfChanged(ref _permissions, value);
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
    
    private string _email = "";
    public string Email
    {
        get => _email;
        set => this.RaiseAndSetIfChanged(ref _email, value);
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
    
    //private List<Permission>? _selectedPermissions;

    private SelectionModel<Permission> _permissionSelection;
    public SelectionModel<Permission> PermissionSelection
    {
        get => _permissionSelection;
        set => this.RaiseAndSetIfChanged(ref _permissionSelection, value);
    }

    public ReactiveCommand<Unit, Unit> BtSelectAllClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCleanAllClicked { get; }
    public ReactiveCommand<Unit, Unit> BtAddUserClicked { get; }
    public ReactiveCommand<Unit, Unit> BtSaveTeamClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteTeamClicked { get; }
    public ReactiveCommand<Unit, Unit> BtAddTeamClicked { get; }
    public ReactiveCommand<Window, Unit> BtSaveClicked { get; }
    public ReactiveCommand<Window, Unit> BtDeleteClicked { get; }
    public ReactiveCommand<Unit, Unit> BtAddProfileClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteProfileClicked { get; }
    public ReactiveCommand<Unit, Unit> BtSaveProfileClicked { get; }
    public ReactiveCommand<Unit, Unit> BtChangePasswordClicked { get; }
    
    
    #endregion

    #region PRIVATE
        private readonly IUsersService _usersService = GetService<IUsersService>();
        private readonly IAuthenticationService _authenticationService = GetService<IAuthenticationService>();
        private readonly IRolesService _rolesService = GetService<IRolesService>();
        private readonly IDialogService _dialogService = GetService<IDialogService>();
        private bool _initialized;
        
        private ITeamsService TeamsService { get; }

    #endregion
    
    #region CONSTRUCTOR
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
        StrPermissions = Localizer["Permissions"];
        StrSelectAll = Localizer["SelectAll"];
        StrCleanAll = Localizer["CleanAll"];
        StrTeams = Localizer["Teams"];
        StrTeamMembers = Localizer["TeamMembers"];
        StrRolePermissions = Localizer["RolePermissions"];
        

        //_selectedPermissions = new List<Permission>();
        _permissionSelection = new SelectionModel<Permission>
        {
            SingleSelect = false
        };

        TeamsService = GetService<ITeamsService>();

        _users = new ObservableCollection<UserListing>();
        _usersService.UserAdded += (_, user) => _users.Add(user.User!);        
        AuthenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
        };
        
        BtSelectAllClicked = ReactiveCommand.Create(() =>
        {
            PermissionSelection.SelectAll();
        });
        
        BtCleanAllClicked = ReactiveCommand.Create(() =>
        {
            PermissionSelection.DeselectRange(0, ((IEnumerable<Permission>)PermissionSelection.Source!).Count());
        });
        
        BtSaveClicked = ReactiveCommand.Create<Window>(ExecuteSave);
        BtAddUserClicked = ReactiveCommand.Create(ExecuteAddUser);
        BtDeleteClicked = ReactiveCommand.Create<Window>(ExecuteDelete);
        BtSaveTeamClicked = ReactiveCommand.Create(ExecuteSaveTeam);
        BtDeleteTeamClicked = ReactiveCommand.Create(ExecuteDeleteTeam);
        BtAddTeamClicked = ReactiveCommand.Create(ExecuteAddTeam);
        BtAddProfileClicked = ReactiveCommand.Create(ExecuteAddProfile);
        BtDeleteProfileClicked = ReactiveCommand.Create(ExecuteDeleteProfile);
        BtSaveProfileClicked = ReactiveCommand.Create(ExecuteSaveProfile);
        BtChangePasswordClicked = ReactiveCommand.CreateFromTask(ExecuteChangePassword);
        
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
        
        this.ValidationRule(
            viewModel => viewModel.Email, 
            email => !string.IsNullOrWhiteSpace(email),
            Localizer["PleaseEnterAValueMSG"]);
        
        IObservable<bool> emailValid =
            this.WhenAnyValue(
                x => x.Email,
                (email) =>
                {
                    Regex regex = new Regex(@"^([\w\.\-]+)@([\w\-]+)((\.(\w){2,3})+)$");
                    Match match = regex.Match(email);
                    if (match.Success)
                        return true;
                    else
                        return false;

                });
        
        this.ValidationRule(
            viewModel => viewModel.Email,
            emailValid,
            Localizer["EnterAValidEmailMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.Username, 
            uname => !string.IsNullOrWhiteSpace(uname),
            Localizer["PleaseEnterAValueMSG"]);
        
        IObservable<bool> usernameUnique =
            this.WhenAnyValue(
                x => x.Username,
                (username) =>
                {
                    if ( _originalUserName == username) return true;
                    if(Users == null) return false;
                    var found = Users.ToList().Find(x => x.Username == username);
                    return found == null;
                });
        
        this.ValidationRule(
            viewModel => viewModel.Username,
            usernameUnique,
            Localizer["UsernameMustBeUniqueMSG"]);

    }
    
    #endregion

    #region METHODS

    private async Task ExecuteChangePassword()
    {
       
        var dialogPassword = await _dialogService.ShowDialogAsync<StringDialogResult>(nameof(ChangePasswordDialogViewModel));
        if (dialogPassword == null) return;
        if (dialogPassword.Action == ResultActions.Ok)
        {
            try
            {
                _usersService.ChangePassword(SelectedUser!.Id, dialogPassword.Result!);
            }
            catch (Exception ex)
            {
                var msgError = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorChangingPasswordMSG"] + " " + ex.Message,
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgError.ShowAsync();
            }
            
        }

    }
    
    private async void ExecuteDelete(Window baseWindow)
    {
        var currentUserId = _authenticationService.GetAuthenticatedUserInfo();

        if (SelectedUser == null)
        {
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["FirstSelectAUserMSG"] ,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
            return;
        }
        
        if (SelectedUser.Id == currentUserId)
        {
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["YouCannotDeleteYourselfMSG"] ,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
            return;
        }

        var msgWarning = MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["AreYouSureToDeleteThisUserMSG"] ,
                Icon = Icon.Warning,
                ButtonDefinitions = ButtonEnum.YesNo,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });
        var result = await msgWarning.ShowAsync();
        if (result == ButtonResult.No) return;
        
        try
        {
            _usersService.DeleteUser(SelectedUser.Id);
            Users?.Remove(SelectedUser);
            SelectedUser = null;
        }
        catch (Exception ex)
        {
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["UnexpectedErrorMSG"] + "\n" + ex.Message ,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
        }
    }

    private async void ExecuteSaveTeam()
    {
        try
        {
            
            var selectedUsersIds = SelectedTeamUsers?.Select(u => int.Parse(u.Key)).ToList();
            if (selectedUsersIds == null) return;
            if (SelectedTeam == null) return;
            TeamsService.UpdateUsers(SelectedTeam.Value, selectedUsersIds);
            
            var msgSuccess = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Success"],
                    ContentMessage = Localizer["TeamSavedMSG"] ,
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSuccess.ShowAsync();
            
        }catch(Exception e)
        {
            Console.WriteLine(e);
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["PleaseCorrectTheErrorsMSG"] + " " + e.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
        }

    }

    private async void ExecuteSaveProfile()
    {
        try
        {
            
            var selectedPermissionIds = SelectedProfilePermissions?.Select(s => Int32.Parse(s.Key)).ToList();
            if (selectedPermissionIds == null) return;
            if (SelectedProfile == null) return;
            
            var selectedPermissionKeys = Permissions?.Where(p => selectedPermissionIds.Contains(p.Id)).Select(p => p.Key).ToList();
            if(selectedPermissionKeys == null) return;
            
            _rolesService.UpdateRolePermissions(SelectedProfile.Value, selectedPermissionKeys); 
            
            var msgSuccess = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Success"],
                    ContentMessage = Localizer["ProfileSavedMSG"] ,
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSuccess.ShowAsync();
            
        }catch(Exception e)
        {
            Console.WriteLine(e);
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["PleaseCorrectTheErrorsMSG"] + " " + e.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
        }
    }
    
    private async void ExecuteAddTeam()
    {
        
        var parameter = new StringDialogParameter()
        {
            Title = Localizer["EditTeam"],
            FieldName = Localizer["TeamName"]
        };
        
        var dialogEdit = await _dialogService.ShowDialogAsync<StringDialogResult, StringDialogParameter>(nameof(EditSingleStringDialogViewModel), parameter);
        
        if(dialogEdit == null) return;

        if (dialogEdit.Action != ResultActions.Ok) return;

        try
        {
            var newTeam = new Team()
            {
                Name = dialogEdit.Result!,
                Users = new List<User>(),
                Value = 0
            };


            var team = TeamsService.Create(newTeam);
            Teams?.Add(team);
            SelectedTeam = team;
        }
        catch (Exception ex)
        {
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErroAddingTeamMSG"] + " " + ex.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
        }


    }

    private async void ExecuteDeleteTeam()
    {
        var msgConfirm = MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Confirmation"],
                ContentMessage = Localizer["AreYouSureToDeleteThisTeamMSG"] ,
                Icon = Icon.Question,
                ButtonDefinitions = ButtonEnum.YesNoAbort,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });

        var result = await msgConfirm.ShowAsync();
        
        if(result != ButtonResult.Yes) return;
        
        
        try
        {
            if(SelectedTeam == null) return;
            TeamsService.Delete(SelectedTeam.Value);
            
            var msgSuccess = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Success"],
                    ContentMessage = Localizer["TeamDeletedMSG"] ,
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSuccess.ShowAsync();
            
            Teams?.Remove(SelectedTeam);
            
            SelectedTeam = null;
            
        }catch(Exception e)
        {
            Console.WriteLine(e);
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorDeletingMSG"] + " " + e.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
        }
    }

    private async void ExecuteAddProfile()
    {
        var parameter = new StringDialogParameter()
        {
            Title = Localizer["EditProfile"],
            FieldName = Localizer["ProfileName"]
        };
        
        var dialogEdit = await _dialogService.ShowDialogAsync<StringDialogResult, StringDialogParameter>(nameof(EditSingleStringDialogViewModel), parameter);
        
        if(dialogEdit == null) return;

        if (dialogEdit.Action != ResultActions.Ok) return;

        try
        {
            var newRole = new Role()
            {
                Name = dialogEdit.Result!,
                Users = new List<User>(),
                Permissions = new List<Permission>(),
                Admin = false,
                Default = null,
                Value = 0
            };


            var role = _rolesService.Create(newRole);
            Profiles?.Add(role);
            SelectedProfile = role;
        }
        catch (Exception ex)
        {
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorCreatingRoleMSG"] + " " + ex.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
        }
    }
    
    private async void ExecuteDeleteProfile()
    {
        var msgConfirm = MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Confirmation"],
                ContentMessage = Localizer["AreYouSureToDeleteThisProfileMSG"] ,
                Icon = Icon.Question,
                ButtonDefinitions = ButtonEnum.YesNoAbort,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });

        var result = await msgConfirm.ShowAsync();
        
        if(result != ButtonResult.Yes) return;
        
        
        try
        {
            if(SelectedProfile == null) return;
            _rolesService.Delete(SelectedProfile.Value);
            
            var msgSuccess = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Success"],
                    ContentMessage = Localizer["ProfileDeletedMSG"] ,
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSuccess.ShowAsync();
            
            Profiles?.Remove(SelectedProfile);
            
            SelectedProfile = null;
            
        }catch(Exception e)
        {
            Console.WriteLine(e);
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorDeletingMSG"] + " " + e.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
        }
    }
    
    private async void ExecuteSave(Window baseWindow)
    {

        //var valid = ValidationContext.Validations.FirstOrDefault(x => !x.IsValid);

        var valid = ValidationContext.Validations.Items.FirstOrDefault(x => !x.IsValid);
        
        if (valid != null)
        {
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["PleaseCorrectTheErrorsMSG"] ,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
            return;
        }
        
        User ??= new UserDto();
        User.UserName = Username!;
        User.Email = Email;
        User.RoleId = SelectedRole!.Value;
        if(SelectedManager != null) User.Manager = SelectedManager!.Id;
        User.Name = Name!;
        User.Type = SelectedAuthenticationMethod!.Name!.ToLower();
        
        _authenticationService.GetAuthenticatedUserInfo();
        var loggedUserId = _authenticationService.AuthenticatedUserInfo!.UserId;
        
        if(User.Id == loggedUserId  && !User.Enabled)
        {
            
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["YouCannotDisableYourselfMSG"] ,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
            return;
        }
        if(User.Id == loggedUserId && !User.Admin)
        {
            
            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["RemovingYourAdminRightsMSG"] ,
                    Icon = Icon.Warning,
                    ButtonDefinitions = ButtonEnum.YesNo,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            var result = await msgError.ShowAsync();
            if (result == ButtonResult.No) return;
        }
        
        if (User.Id == 0)
        {
            User = _usersService.CreateUser(User);
        }
        else
        {
            _usersService.SaveUser(User);
        }

        if(_permissionSelection.Source != null )
            _usersService.SaveUserPermissions(User.Id, _permissionSelection.SelectedItems.ToList());
        else 
            _usersService.SaveUserPermissions(User.Id, new List<Permission?>());

        
        SelectedUser = Users?.ToList().FirstOrDefault(u => u.Id == User.Id);
        
        var msgInfo = MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Success"],
                ContentMessage = Localizer["UserCreatedSuccessfully"] ,
                Icon = Icon.Success,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });
        await msgInfo.ShowAsync();
    }
    
    private void ExecuteAddUser()
    {
        SelectedUser = new UserListing()
        {
            Id = 0,
            Name = "",
            Username = "",
        };
    }
    
    public async void Initialize()
    {
        if (_initialized) return;
        await Task.Run(async () =>
        {
            Users = new ObservableCollection<UserListing>(await _usersService.GetAllAsync());
            AuthenticationMethods = AuthenticationService.GetAuthenticationMethods();
            Roles = await _rolesService.GetAllRolesAsync();
            Permissions = new ObservableCollection<Permission>(await _usersService.GetAllPermissionsAsync());
            Teams = new ObservableCollection<Team>(await TeamsService.GetAllAsync());
            Profiles = new ObservableCollection<Role>(await _rolesService.GetAllRolesAsync());
        });
        _initialized = true;
    }
    #endregion
    
}