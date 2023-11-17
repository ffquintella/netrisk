using System.Text;
using AutoMapper;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
using Model.Exceptions;
using Model.Users;
using ServerServices.EmailTemplates;
using ServerServices.Interfaces;
using SharedServices.Interfaces;
using Tools;
using ILogger = Serilog.ILogger;
using static BCrypt.Net.BCrypt;
using System.Text.Json;
using ServerServices.Services;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")] 
[ApiController]
[Route("[controller]")]
public class UsersController: ApiBaseController
{
    
    #region PRIVATE 
    
    private readonly IMapper _mapper;
    private readonly IEmailService _emailService;
    private readonly ILanguageManager _languageManager;
    private readonly ILinksService _linksService;
    private readonly IConfiguration _configuration;
    private readonly IPermissionsService _permissionsService;

    #endregion

    public UsersController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IMapper mapper,
        IEmailService emailService,
        ILinksService linksService,
        ILanguageManager languageManager,
        IPermissionsService permissionsService,
        ITeamsService teamsService,
        IConfiguration configuration) : base(logger, httpContextAccessor, usersService)
    {
        _mapper = mapper;
        _emailService = emailService;
        _languageManager = languageManager;
        _linksService = linksService;
        _configuration = configuration;
        _permissionsService = permissionsService;
    }
    
    
    
    
    /// <summary>
    /// Gets one user by id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
     
    [HttpGet]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<UserDto> GetUser(int id)
    {
        
        try
        {
            var user  = UsersService.GetUserById(id);
            if (user == null) return StatusCode(StatusCodes.Status500InternalServerError, "Error looking for the user");
            var userDto = _mapper.Map<UserDto>(user);
            return Ok(userDto);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The user with id: {Id} was not found: {Message}", id, ex.Message);
            return NotFound($"The user with the id:{ex.Identification} was not found");
        }
        
    }
    
    /// <summary>
    /// Delete one user by id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpDelete]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult DeleteUser(int id)
    {
        var loggedUser = GetUser();
        
        try
        {
            if (loggedUser.Value == id)
            {
                return BadRequest("You can not delete yourself");
            }
            
            UsersService.DeleteUser(id);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The user with id: {Id} was not found: {Message}", id, ex.Message);
            return NotFound($"The user with the id:{ex.Identification} was not found");
        }
        catch (Exception ex)
        {
            Logger.Warning("Unexpected error while deleting user:{Id} message:{Message}", id, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, $"Unexpected error while deleting user:{id}");
        }
    }
    
    /// <summary>
    /// Gets one user´s permissions by id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("{id}/permissions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Permission>> GetUserPermissions(int id)
    {
        
        try
        {
            var permissions = _permissionsService.GetUserPermissionsById(id);

            return Ok(permissions);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The user with id: {Id} was not found: {Message}", id, ex.Message);
            return NotFound($"The user with the id:{ex.Identification} was not found");
        }
        
    }
    
    /// <summary>
    /// Save a user´s permissions 
    /// </summary>
    /// <param name="id"></param>
    /// <param name="permissionIds"></param>
    /// <returns></returns>
    [HttpPut]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("{id}/permissions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Permission>> SaveUserPermissions(int id, [FromBody] List<int> permissionIds)
    {
        
        try
        {
            _permissionsService.SaveUserPermissionsById(id, permissionIds);

            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The user with id: {Id} was not found: {Message}", id, ex.Message);
            return NotFound($"The user with the id:{ex.Identification} was not found");
        }
        catch (Exception ex)
        {
            Logger.Warning("Unexpected error while saving permissions for user:{Id} message:{Message}", id, ex.Message);
            return NotFound($"Unexpected error while saving permissions for user:{id}");
        }
        
    }
    
    /// <summary>
    /// Gets one user´s permissions by id
    /// </summary>

    /// <returns></returns>
    [HttpGet]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("permissions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Permission>> GetAllPermissions()
    {
        
        try
        {
            var permissions = _permissionsService.GetAllPermissions();

            return Ok(permissions);
        }
        catch (Exception ex)
        {
            Logger.Error("Unexpected error listing permissions message:{Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unexpected error listing permissions");
            
        }
        
    }
    
    /// <summary>
    /// Saves the user 
    /// </summary>
    /// <param name="id">Id of the user to be saved</param>
    /// <param name="user">User Object </param>
    /// <returns></returns>
    [HttpPut]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult SaveUser(int id, [FromBody] UserDto user)
    {
        
        try
        {
            if (id != user.Id) return StatusCode(StatusCodes.Status400BadRequest);
            //Let´s find the user 
            var userEntity = UsersService.GetUserById(id);
            if (userEntity == null) return StatusCode(StatusCodes.Status500InternalServerError, "Error looking for the user");


            // make sure the username is lowercase
            user.UserName = user.UserName.ToLower();
            
            // Now let´s update the user
            var finalUser = _mapper.Map<User>(user);
            
            
            //Coping values 
            
            finalUser.Password = userEntity.Password;
            finalUser.Salt = userEntity.Salt;
            
            UsersService.SaveUser(finalUser);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The user with Id:{Id} was not found Message:{Message}", id, ex.Message);
            return NotFound($"The user with the id:{ex.Identification} was not found");
        }
        catch (Exception ex)
        {
            Logger.Warning("Internal error while saving user with Id:{Id} Message:{Message}", id, ex.Message);
            return NotFound($"Internal error while saving user:{id}");
        }
        
    }

    /// <summary>
    /// Creates a new user
    /// </summary>
    /// <param name="user">The user object</param>
    /// <returns></returns>
    [HttpPost]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(UserDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<UserDto> CreateUser([FromBody] UserDto user)
    {
        var callingUser = GetUser();
        if (user.Id != 0)
        {
            Logger.Warning("Invalid Id in user creation attempt from user: {User}", callingUser.Value);
            return StatusCode(StatusCodes.Status400BadRequest, "Invalid Id");
            
        }
        
        //Check if user already exists 
        var existingUser = UsersService.GetUser(user.UserName);
        if (existingUser != null)
        {
            Logger.Warning("User already exists in user creation attempt from user: {User}", callingUser.Value);
            return StatusCode(StatusCodes.Status400BadRequest, "User already exists");
        }

        if (user == null) throw new Exception("User cannot be null here");
        
        //Let´s check if user language is valid
        if(String.IsNullOrEmpty(user.Lang)) user.Lang = _languageManager.DefaultLanguage.Code.ToLower();
        else
        {
            var lang = _languageManager.AllLanguages.FirstOrDefault(l =>
                string.Equals(l.Code.ToLower(), user.Lang.ToLower(), StringComparison.Ordinal));
            if(lang == null)
            {
                Logger.Warning("Invalid language in user creation attempt from user: {User}", callingUser.Value);
                return StatusCode(StatusCodes.Status400BadRequest);
            }
        }
        
        try
        {
            var usr = _mapper.Map<User>(user);
            
            // SET temporary password 

            var password = RandomGenerator.RandomString(12);
            
            
            usr.Password = Encoding.UTF8.GetBytes(HashPassword(password, 15));
            
            var newUser = UsersService.CreateUser(usr);
            var newUserDto = _mapper.Map<UserDto>(newUser);

            var linkDataObj = new PasswordResetLinkData()
            {
                UserId = newUserDto.Id
            };
            
            var linkData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(linkDataObj));
            
            //Email Parameters
            var emailParameters = new UserCreated {
                Name = newUserDto.Name, 
                Link = _linksService.CreateLink("passwordReset", 
                    DateTime.Now.AddDays(Double.Parse(_configuration["links:passwordResetDuration"]!)), linkData),
            };
            
            if(user.Type != "saml")
            {
                //Send email
                _emailService.SendEmailAsync(user.Email, "User created", "UserCreated", user.Lang.ToLower(), emailParameters);
            }


            return newUserDto;
        }
        catch (DataAlreadyExistsException ex)
        {
            Logger.Warning("The user with Id:{Id} already exists Message:{Message}", user.Id, ex.Message);
            return BadRequest("User already exists");
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error creating user with Id:{Id}  Message:{Message}", user.Id, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError,"Unknown error creating user");
        }

        
    }


    
    [HttpGet]
    [Route("Name/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<string> GetUserName(int id)
    {
        
        try
        {
            var name  = UsersService.GetUserName(id);
            return Ok(name);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The user with id: {Id} was not found: {Message}", id, ex.Message);
            return NotFound($"The user with the id:{ex.Identification} was not found");
        }
        
    }
    
    
    [HttpPost]
    [Route("{id}/ChangePassword")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<string> ChangePassword(int id, [FromBody] ChangePasswordRequest? changePasswordRequest)
    {
        if (changePasswordRequest == null)
            return BadRequest("The request is empty");
        
        var loggedUser = GetUser();

        if (!loggedUser.Admin)
        {
            // Then the user can only change it´s own password
            if (loggedUser.Value != id)
            {
                Logger.Warning("Invalid permission while trying to change password of: {Id} ", id);
                return Unauthorized($"The user with the id:{loggedUser.Value} is not authorized to change the password of {id}");
            }
            
            // Now let´s verify if the old password is correct
            
            if (!UsersService.VerifyPassword(id, changePasswordRequest.OldPassword))
            {
                Logger.Warning("The user with id: {Id} was not found: {LoggedUserValue} while trying to change it´s password", id, loggedUser.Value);
                return Unauthorized($"The user with the id:{loggedUser.Value} is not authorized to change the password of {id}");
            }
        }
        
        // Let´s check if the user exists 
        var user = UsersService.GetUserById(id);

        if (user == null) NotFound($"The user indicated was not found");
        
        // If we are here we can change the password 
        try
        {
            var okResult = UsersService.ChangePassword(id, changePasswordRequest.NewPassword);
            if (okResult)
            {
                Logger.Information("Password changed for user {Id}", id);
                return Ok("Password changed");
            }
            
            Logger.Error("Error changing password for user {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error changing password");

        }
        catch (Exception ex)
        {
            Logger.Warning("Error changing the password of the user {Id}: {Message}", id, ex.Message);
            return NotFound($"The password could not be changed");
        }
        
    }
    
    //listings
    [HttpGet]
    [Route("Listings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<UserListing>> ListUsers()
    {
        
        try
        {
            var users = UsersService.ListActiveUsers();
            return Ok(users);
        }
        catch (Exception ex)
        {
            Logger.Warning("Error listing users: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error listing user");
        }
        
    }
}