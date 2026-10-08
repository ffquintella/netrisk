using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;
using Mapster;

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
    /// Records a management review in the caller's name, through the enforced service path
    /// (<see cref="IMgmtReviewsService.CreateReviewAsync"/>): the third-line guard, segregation of duties
    /// (nobody reviews a risk they submitted, own or manage — administrators included) and the appetite's
    /// dual-approval threshold. The acting user is always the caller; a reviewer named in the payload is ignored.
    ///
    /// A refusal is 422 naming the rule, as the other governance decisions answer it (<c>RiskGovernanceController</c>):
    /// the caller may review risks in general, and it is their relation to <em>this</em> risk that makes the request
    /// impossible — a 403 would send them to ask for a permission that would not help.
    /// </summary>
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(MgmtReview))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MgmtReview>> Create([FromBody] MgmtReviewDto review)
    {
        var user = GetUser();

        if(review.Id > 0 ) review.Id = 0;

        try
        {
            review.Reviewer = user.Value;

            var reviewObj = review.Adapt<MgmtReview>();

            var newReview = await _mgmtReviewsService.CreateReviewAsync(reviewObj, user.Value);

            Logger.Information("User:{UserValue} created mgmtReview {Id} on risk {RiskId}", user.Value,
                newReview.Id, newReview.RiskId);

            return Created($"MgmtReviews/{newReview.Id}", newReview);
        }
        catch (RuleBrokenException ex)
        {
            return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
        }
        catch (PermissionInvalidException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "insufficient_authority", ex.Permission, ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error creating mgmtReview: {Message}", ex.Message);
            return StatusCode(500);
        }
    }

    [HttpPut]
    [Route("{reviewId}")]
    [Authorize(Policy = "RequireMgmtReviewAccess")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MgmtReview))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MgmtReview>> Create(int reviewId, [FromBody] MgmtReviewDto review)
    {
        var user = GetUser();

        if (reviewId <= 0) return BadRequest("reviewId must be greater than 0");

        MgmtReview upReview;
        
        try
        {
            review.Id = reviewId;
            upReview = await _mgmtReviewsService.UpdateAsync(review, user.Value);

            Logger.Information("User:{UserValue} updated mgmtReview {Id}", user.Value, reviewId);
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (RuleBrokenException ex)
        {
            return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
        }
        catch (PermissionInvalidException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "insufficient_authority", ex.Permission, ex.Message });
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error updating mgmtReview {Id}: {Message}", reviewId, ex.Message);
            return StatusCode(500);
        }

        return Ok(upReview);
    }
    
    
    [HttpGet]
    [Route("{reviewId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MgmtReview))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<MgmtReview> GetOne(int reviewId)
    {
        var user = GetUser();

        if (reviewId <= 0) return BadRequest("reviewId must be greater than 0");
        
        Logger.Information("User:{UserValue} got mgmtReview {Id}", user.Value, reviewId);

        MgmtReview review;
        
        try
        {
            review = _mgmtReviewsService.GetOne(reviewId);
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting mgmtReview: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(review);
    }
    

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
