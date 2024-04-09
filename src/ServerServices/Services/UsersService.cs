using System.Text;
using AutoMapper;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Model.Exceptions;
using Model.DTO;
using ServerServices.Interfaces;
using static BCrypt.Net.BCrypt;
using static Tools.Extensions.StringExt;

namespace ServerServices.Services;

public class UsersService: IUsersService
{
    //private SRDbContext? _dbContext = null;
    private DALService? _dalService;
    private ILogger _log;
    private IRolesService _rolesService;
    private IMapper _mapper;
    private readonly IPermissionsService _permissions;

    public UsersService(DALService dalService,
        ILoggerFactory logger,
        IRolesService rolesService,
        IMapper mapper,
        IPermissionsService permissionsService)
    {
        _dalService = dalService;
        _log = logger.CreateLogger(nameof(UsersService));
        _rolesService = rolesService;
        _permissions = permissionsService;
        _mapper = mapper;
    }

    public User? GetUser(string userName)
    {
        using var dbContext = _dalService!.GetContext(false);
        var user = dbContext?.Users?
            .Where(u => u.Username == Encoding.UTF8.GetBytes(userName.ToLower()))
            .FirstOrDefault();

        return user;
    }

    public async Task<List<User>> GetAllAsync()
    {
        await using var dbContext = _dalService!.GetContext(false);
        
        var users = await dbContext.Users.ToListAsync();
        //if (users == null) return new List<User>();
        return users;
    }

    public async Task<List<User>> GetByTeamIdAsync(int teamId)
    {
        await using var dbContext = _dalService!.GetContext(false);
        
        var users = await dbContext.Users
            .Include(u => u.Teams)
            .Where(u => u.Teams.Any(t => t.Value == teamId) )
            .ToListAsync();
        
        //if (users == null) return new List<User>();
        return users; 
    }

    public bool VerifyPassword(string username, string password)
    {
        return VerifyPassword(GetUser(username), password);
    }
    public bool VerifyPassword(int userId, string password)
    {
        return VerifyPassword(GetUserById(userId), password);
    }

    public bool VerifyPassword(User? user, string password)
    {
        if (user == null) return false;
        
        if (user.Type == "saml")
        {
            throw new Exception("Cannot verify password of saml users");
        }
        
        if(user.Lockout == 1)
        {
            return false;
        }
        
        var valid = Verify(password, Encoding.UTF8.GetString(user.Password));
        
        return valid; 
    }

    public void RegisterLogin(int userId, string ipAddress)
    {
        using var dbContext = _dalService!.GetContext();

        var user = dbContext.Users.Find(userId); 
            
        if (user == null) return;
        
        user.LastLogin = DateTime.Now;

        dbContext.SaveChanges();
        
        _log.LogInformation("User {UserId} logged in from {IpAddress}", userId, ipAddress);
        
    }

    public bool ChangePassword(int userId, string password)
    {
        using var dbContext = _dalService!.GetContext();

        var user = GetUserById(userId);

        if (user == null) return false;
        if (user.Type == "saml")
        {
            throw new Exception("Cannot change password of saml users");
        } 

        
        try
        {
            //var salt = GenerateSalt();
            //user.Salt = salt.Replace("$2a$", "").Truncate(20, "");
            user.Password = Encoding.UTF8.GetBytes(HashPassword(password, 15));
            dbContext?.Users?.Update(user);
            dbContext?.SaveChanges();
            _log.LogWarning("Password changed for user {userId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error changing password for user {userId}", userId);
        }

        
        return false;
    }
    public User? GetUserById(int userId)
    {
        using var dbContext = _dalService!.GetContext();
        var user = dbContext?.Users?
            .Where(u => u.Value == userId)
            .FirstOrDefault();
      
        
        return user;
    }

    public void SaveUser(User user)
    {
        using var dbContext = _dalService!.GetContext();
        var dbUser = dbContext?.Users?.Find(user.Value);
        if(dbUser == null) throw new DataNotFoundException("user", user.Value.ToString());
        
        _mapper.Map(user, dbUser);
        dbContext?.SaveChanges();
        
        //dbContext?.Users?.Update(dbUser);

    }

    public String GetUserName(int id)
    {
        var user = GetUserById(id);
        if (user == null)
        {
            throw new DataNotFoundException("user", id.ToString());
        }
        return user.Name;
    }

    public void DeleteUser(int userId)
    {
        using var dbContext = _dalService!.GetContext();
        var dbUser = dbContext?.Users?.Find(userId);

        if (dbUser == null)
        {
            throw new DataNotFoundException("user", userId.ToString());
        }
        
        dbContext?.Users?.Remove(dbUser);
        dbContext?.SaveChanges();


    }

    // List all active users
    public List<UserListing> ListActiveUsers()
    {
        var list = new List<UserListing>();
        
        using var dbContext = _dalService!.GetContext();
        var users = dbContext?.Users?
            .Where(u => u.Enabled == true)
            .ToArray();
        if (users == null) return list;
        
        foreach (var user in users)
        {
            var ul = new UserListing
            {
                Id = user.Value,
                Name = user.Name,
                Username = Encoding.UTF8.GetString(user.Username)
            };
            list.Add(ul);
        }
        
        return list;
    }
    
    public List<string> GetUserPermissions(int userId)
    {
        var user = GetUserById(userId);
        if (user == null)
        {
            throw new UserNotFoundException();
        }

        return _permissions.GetUserPermissions(user);
    }

    public User CreateUser(User user)
    {
        using var dbContext = _dalService!.GetContext();
        
        
        var dbUser = dbContext?.Users?.Find(user.Value);
        
        if(dbUser != null) throw new DataAlreadyExistsException("local", "user", user.Value.ToString(), "User already exists");

        var username = Encoding.UTF8.GetString(user.Username);
        
        user.Username = Encoding.UTF8.GetBytes(username.ToLower());

        
        foreach (var per in user.Permissions)
        {
            dbContext!.Permissions.Update(per);
        }
       
        
        var newUser = dbContext?.Users?.Add(user);

        if (newUser == null) throw new Exception("Unknown error creating user");
        
        dbContext?.SaveChanges();

        return newUser!.Entity;
    }

    public bool CheckPasswordComplexity(string password)
    {
        return Tools.Security.PasswordTools.CheckPasswordComplexity(password);
    }

}