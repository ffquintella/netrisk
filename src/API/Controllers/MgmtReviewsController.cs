using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireMgmtReviewAccess")]
[ApiController]
[Route("[controller]")]
public class MgmtReviewsController: ApiBaseController
{
    private IRisksService _risksService;
    private readonly IMgmtReviewsService _mgmtReviewsService;
    
    public MgmtReviewsController(
        ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IMgmtReviewsService mgmtReviewsService,
        IRisksService risksService) : base(logger, httpContextAccessor, usersService)
    {
        _risksService = risksService;
        _mgmtReviewsService = mgmtReviewsService;
    }

    /// <summary>
    /// Gets a mitigation by risk ID
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <returns></returns>
    [HttpGet]
    [Route("Types")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Review>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Review>> GetTypes()
    {
        var user = GetUser();

        Logger.Information("User:{UserValue} got review types list", user.Value);

        List<Review> reviews;
        
        try
        {
            reviews = _mgmtReviewsService.GetReviewTypes();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting review types list: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(reviews);
    }
    
    [HttpGet]
    [Route("NextSteps")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Review>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<NextStep>> GetNextSteps()
    {
        var user = GetUser();

        Logger.Information("User:{UserValue} got review next steps list", user.Value);

        List<NextStep> nextSteps;
        
        try
        {
            nextSteps = _mgmtReviewsService.GetNextSteps();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting review next steps list: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(nextSteps);
    }
    
}