using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Admin;

/// <summary>
/// One row of the assignment grid: the stored assignment plus the names its ids stand for.
/// </summary>
/// <summary>An entity as offered in the grant dropdown, with its name already resolved.</summary>
public class EntityChoice
{
    public int Id { get; init; }
    public string Display { get; init; } = string.Empty;
}

public class EntityRoleAssignment
{
    public int Id { get; init; }
    public string EntityName { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string Granted => CreatedAt.ToLocalTime().ToString("g");
}

/// <summary>
/// Per-entity role administration (Track 2 milestone 2.3.2): which business entities a user may
/// see, and in what role.
///
/// This is the surface that makes the multi-tenant model usable — the enforcement it drives lives
/// in the model's query filters and the write guard, and a user with no assignment here sees
/// nothing at all, which is the intended deny-by-default.
/// </summary>
public class EntityAccessViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle => Localizer["Entity Access"];
    public string StrSubtitle => Localizer["Which business entities each user may see"];
    public string StrUser => Localizer["User"];
    public string StrEntity => Localizer["Entity"];
    public string StrRole => Localizer["Role"];
    public string StrGranted => Localizer["Granted"];
    public string StrAssign => Localizer["Assign"];
    public string StrRevoke => Localizer["Revoke"];
    public string StrRefresh => Localizer["Refresh"];
    public string StrAssignments => Localizer["Assignments"];
    public string StrNoUserSelected => Localizer["Select a user to manage their entity access"];
    public string StrNoAssignments => Localizer["This user has no entity assignment and therefore sees no data"];
    public string StrAssigned => Localizer["Entity access granted."];
    public string StrRevoked => Localizer["Entity access revoked."];

    #endregion

    #region PROPERTIES

    private IUserAccessService UserAccessService { get; } = GetService<IUserAccessService>();
    private IUsersService UsersService { get; } = GetService<IUsersService>();
    private IRolesService RolesService { get; } = GetService<IRolesService>();
    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();

    private ObservableCollection<UserListing> _users = new();
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
            this.RaisePropertyChanged(nameof(HasSelectedUser));
            _ = LoadAssignmentsAsync();
        }
    }

    public bool HasSelectedUser => SelectedUser != null;

    private ObservableCollection<EntityChoice> _entities = new();
    public ObservableCollection<EntityChoice> Entities
    {
        get => _entities;
        set => this.RaiseAndSetIfChanged(ref _entities, value);
    }

    private EntityChoice? _selectedEntity;
    public EntityChoice? SelectedEntity
    {
        get => _selectedEntity;
        set => this.RaiseAndSetIfChanged(ref _selectedEntity, value);
    }

    private ObservableCollection<Role> _roles = new();
    public ObservableCollection<Role> Roles
    {
        get => _roles;
        set => this.RaiseAndSetIfChanged(ref _roles, value);
    }

    private Role? _selectedRole;
    public Role? SelectedRole
    {
        get => _selectedRole;
        set => this.RaiseAndSetIfChanged(ref _selectedRole, value);
    }

    private ObservableCollection<EntityRoleAssignment> _assignments = new();
    public ObservableCollection<EntityRoleAssignment> Assignments
    {
        get => _assignments;
        set
        {
            this.RaiseAndSetIfChanged(ref _assignments, value);
            this.RaisePropertyChanged(nameof(HasNoAssignments));
        }
    }

    private EntityRoleAssignment? _selectedAssignment;
    public EntityRoleAssignment? SelectedAssignment
    {
        get => _selectedAssignment;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAssignment, value);
            this.RaisePropertyChanged(nameof(HasSelectedAssignment));
        }
    }

    public bool HasSelectedAssignment => SelectedAssignment != null;

    /// <summary>
    /// Drives the warning under the grid. Worth calling out explicitly: an unassigned user is not
    /// "unrestricted", they are locked out, and that surprises people.
    /// </summary>
    public bool HasNoAssignments => HasSelectedUser && Assignments.Count == 0;

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> RefreshCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AssignCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RevokeCommand { get; }

    #endregion

    public EntityAccessViewModel()
    {
        RefreshCommand = ReactiveCommand.CreateFromTask(InitializeAsync);
        AssignCommand = ReactiveCommand.CreateFromTask(AssignAsync);
        RevokeCommand = ReactiveCommand.CreateFromTask(RevokeAsync);
    }

    public async Task InitializeAsync()
    {
        await WithBusyAsync(async () =>
        {
            try
            {
                var users = await UsersService.GetAllAsync(ignoreCache: true);
                var roles = await RolesService.GetAllRolesAsync();
                var entities = await EntitiesService.GetAllAsync();

                var previousUserId = SelectedUser?.Id;

                Users = new ObservableCollection<UserListing>(users.OrderBy(u => u.Name));
                Roles = new ObservableCollection<Role>(roles.OrderBy(r => r.Name));
                Entities = new ObservableCollection<EntityChoice>(entities
                    .Select(e => new EntityChoice { Id = e.Id, Display = EntityDisplayName(e) })
                    .OrderBy(e => e.Display));

                SelectedRole ??= Roles.FirstOrDefault();
                SelectedEntity ??= Entities.FirstOrDefault();
                SelectedUser = Users.FirstOrDefault(u => u.Id == previousUserId) ?? Users.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Logger.Error("Error loading entity access data: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not load the entity access data"]);
            }
        });
    }

    /// <summary>
    /// The entity's display name lives in its properties, not on the row, so fall back to the id
    /// when a definition has no name property.
    /// </summary>
    public static string EntityDisplayName(Entity entity)
    {
        var name = entity.EntitiesProperties?.FirstOrDefault(p => p.Type == "name")?.Value;
        return string.IsNullOrWhiteSpace(name) ? $"#{entity.Id}" : $"{name} (#{entity.Id})";
    }

    private async Task LoadAssignmentsAsync()
    {
        if (SelectedUser == null)
        {
            Assignments = new ObservableCollection<EntityRoleAssignment>();
            return;
        }

        try
        {
            var assignments = await UserAccessService.GetUserEntityRolesAsync(SelectedUser.Id);

            Assignments = new ObservableCollection<EntityRoleAssignment>(
                assignments.Select(a => new EntityRoleAssignment
                {
                    Id = a.Id,
                    EntityName = a.Entity != null ? EntityDisplayName(a.Entity) : $"#{a.EntityId}",
                    RoleName = a.Role?.Name ?? $"#{a.RoleId}",
                    CreatedAt = a.CreatedAt
                }));

            SelectedAssignment = null;
        }
        catch (Exception ex)
        {
            Logger.Error("Error loading entity roles of user {UserId}: {Message}", SelectedUser.Id, ex.Message);
            Toasts.Error(Localizer["Could not load the entity assignments"]);
        }
    }

    private async Task AssignAsync()
    {
        if (SelectedUser == null || SelectedEntity == null || SelectedRole == null)
        {
            Toasts.Warning(Localizer["Pick a user, an entity and a role first"]);
            return;
        }

        await WithBusyAsync(async () =>
        {
            try
            {
                await UserAccessService.AssignEntityRoleAsync(
                    SelectedUser.Id, SelectedEntity.Id, SelectedRole.Value);

                await LoadAssignmentsAsync();
                Toasts.Success(StrAssigned);
            }
            catch (Exception ex)
            {
                Logger.Error("Error assigning entity role: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not grant the entity access"]);
            }
        });
    }

    private async Task RevokeAsync()
    {
        if (SelectedAssignment == null) return;

        await WithBusyAsync(async () =>
        {
            try
            {
                await UserAccessService.RevokeEntityRoleAsync(SelectedAssignment.Id);
                await LoadAssignmentsAsync();
                Toasts.Success(StrRevoked);
            }
            catch (Exception ex)
            {
                Logger.Error("Error revoking entity role: {Message}", ex.Message);
                Toasts.Error(Localizer["Could not revoke the entity access"]);
            }
        });
    }
}
