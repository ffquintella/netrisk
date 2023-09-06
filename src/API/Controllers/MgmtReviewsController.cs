using AutoMapper;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
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
    private IMapper _mapper;
    private readonly IMgmtReviewsService _mgmtReviewsService;
    
    public MgmtReviewsController(
        ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IMgmtReviewsService mgmtReviewsService,
        IMapper mapper,
        IRisksService risksService) : base(logger, httpContextAccessor, usersService)
    {
        _risksService = risksService;
        _mgmtReviewsService = mgmtReviewsService;
        _mapper = mapper;
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MgmtReview))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<MgmtReview> Create([FromBody] MgmtReviewDto review)
    {
        var user = GetUser();

        if(review.Id > 0 ) review.Id = 0;
        
        Logger.Information("User:{UserValue} created new mgmtReview", user.Value);

        MgmtReview newReview;
        
        try
        {
            review.Reviewer = user.Value;

            var reviewObj = _mapper.Map<MgmtReview>(review);
            
            newReview = _mgmtReviewsService.Create(reviewObj);
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error creating mgmtReview: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Created($"MgmtReviews/{newReview.Id}", newReview);
    }

    [HttpPut]
    [Route("{reviewId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MgmtReview))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<MgmtReview> Create(int reviewId, [FromBody] MgmtReviewDto review)
    {
        var user = GetUser();

        if (review.Id <= 0) return BadRequest("reviewId must be greater than 0");
        
        Logger.Information("User:{UserValue} updated mgmtReview {Id}", user.Value, reviewId);

        MgmtReview upReview;
        
        try
        {
            review.Reviewer = user.Value;
            
            upReview = _mgmtReviewsService.Update(review);
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error creating mgmtReview: {Message}", ex.Message);
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