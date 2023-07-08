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

namespace API.Controllers;

[Authorize(Policy = "RequireAdminOnly")]
[ApiController]
[Route("[controller]")]
public class UsersController: ApiBaseController
{
    
    private readonly IMapper _mapper;
    private readonly IEmailService _emailService;
    private readonly ILanguageManager _languageManager;

    public UsersController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IMapper mapper,
        IEmailService emailService,
        ILanguageManager languageManager,
        IUserManagementService userManagementService) : base(logger, httpContextAccessor, userManagementService)
    {
        _mapper = mapper;
        _emailService = emailService;
        _languageManager = languageManager;
    }
    
    /// <summary>
    /// Gets one user by id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<UserDto> GetUser(int id)
    {
        
        try
        {
            var user  = _userManagementService.GetUserById(id);
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
    /// Saves the user 
    /// </summary>
    /// <param name="id">Id of the user to be saved</param>
    /// <param name="user">User Object </param>
    /// <returns></returns>
    [HttpPut]
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
            var userEntity = _userManagementService.GetUserById(id);
            if (userEntity == null) return StatusCode(StatusCodes.Status500InternalServerError, "Error looking for the user");
            
            // Now let´s update the user
            
            var finalUser = _mapper.Map<User>(user);
            
            //Coping values 
            
            finalUser.Password = userEntity.Password;
            finalUser.Salt = userEntity.Salt;
            
            _userManagementService.SaveUser(finalUser);
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
        var existingUser = _userManagementService.GetUser(user.UserName);
        if (existingUser != null)
        {
            Logger.Warning("User already exists in user creation attempt from user: {User}", callingUser.Value);
            return StatusCode(StatusCodes.Status400BadRequest, "User already exists");
        }
        
        //Let´s check if user language is valid
        if(user.Lang == "") user.Lang = _languageManager.DefaultLanguage.Code.ToLower();
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
            
            var newUser = _userManagementService.CreateUser(usr);
            var newUserDto = _mapper.Map<UserDto>(newUser);
            
            //Email Parameters
            
            var emailParameters = new UserCreated {
                Name = newUserDto.Name, 
                Link = "http://kjshdfkjshfdkdsjfh.com"
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


    [Authorize(Policy = "RequireValidUser")]
    [HttpGet]
    [Route("Name/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<string> GetUserName(int id)
    {
        
        try
        {
            var name  = _userManagementService.GetUserName(id);
            return Ok(name);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The user with id: {Id} was not found: {Message}", id, ex.Message);
            return NotFound($"The user with the id:{ex.Identification} was not found");
        }
        
    }
    
    [Authorize(Policy = "RequireValidUser")]
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
            
            if (!_userManagementService.VerifyPassword(id, changePasswordRequest.OldPassword))
            {
                Logger.Warning("The user with id: {Id} was not found: {LoggedUserValue} while trying to change it´s password", id, loggedUser.Value);
                return Unauthorized($"The user with the id:{loggedUser.Value} is not authorized to change the password of {id}");
            }
        }
        
        // Let´s check if the user exists 
        var user = _userManagementService.GetUserById(id);

        if (user == null) NotFound($"The user indicated was not found");
        
        // If we are here we can change the password 
        try
        {
            var okResult = _userManagementService.ChangePassword(id, changePasswordRequest.NewPassword);
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
    [Authorize(Policy = "RequireValidUser")]
    [HttpGet]
    [Route("Listings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<UserListing>> ListUsers()
    {
        
        try
        {
            var users = _userManagementService.ListActiveUsers();
            return Ok(users);
        }
        catch (Exception ex)
        {
            Logger.Warning("Error listing users: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error listing user");
        }
        
    }
}