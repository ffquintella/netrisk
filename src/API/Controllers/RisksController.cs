using System.Text.RegularExpressions;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
using Model.Exceptions;
using Model.Risks;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[ApiController]
[Route("[controller]")]
public class RisksController : ApiBaseController
{

    private IRisksService _risksService;
    private IMitigationsService _mitigations;
    private readonly IFilesService _filesService;
    private readonly IMgmtReviewsService _mgmtReviewsService;

    public RisksController(
        ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IMitigationsService mitigationsService,
        IFilesService filesService,
        IMgmtReviewsService mgmtReviewsService,
        IRisksService risksService) : base(logger, httpContextAccessor, usersService)
    {
        _risksService = risksService;
        _mitigations = mitigationsService;
        _filesService = filesService;
        _mgmtReviewsService = mgmtReviewsService;
    }
    
    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Risk>> GetAll([FromQuery] string? status = null, [FromQuery] bool includeClosed = false)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} listed all risks", user.Value);
        
        //var risks = new List<Risk>();

        try
        {
            
            List<Risk> risks;
            if(!includeClosed)
                risks = _risksService.GetAll(status, notStatus:"Closed");
            else 
                risks = _risksService.GetAll(status, notStatus:null);

            return Ok(risks);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to see risks message: {ExMessage}", user.Name, ex.Message);
            return this.Unauthorized();
        }
        
        
    }


    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("ToReview")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Risk>> GetToReview( [FromQuery] int daysSinceLastReview, [FromQuery] string? status = null, [FromQuery] bool includeNew = false )
    {
        try
        {
            var risks = _risksService.GetToReview(daysSinceLastReview, status, includeNew);
            return Ok(risks);
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting risks: {Message}", ex.Message);
            return StatusCode(500);
        }

        
    }

    /// <summary>
    /// Gets a risk by id
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <returns></returns>
    [HttpGet]
    [Route("{id}")]
    [Authorize(Policy = "RequireValidUser")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Risk> GetRisk(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got risk with id={Id}", user.Value, id);

        Risk risk;
        
        try
        {
            if (user.Admin)
            {
                risk = _risksService.GetRisk(id);
            }else risk = _risksService.GetUserRisk(user, id);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to see risks message: {ExMessage}", user.Name, ex.Message);
            return this.Unauthorized();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The risk: {Id} was not found in the database: {ExMessage}", id, ex.Message);
            return this.NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting risk: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(risk);
    }
    
    /// <summary>
    /// Gets a mitigation by risk ID
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <returns></returns>
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("{id}/Mitigation")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Mitigation>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Mitigation> GetMitigation(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got mitigation for risk with id={Id}", user.Value, id);

        Mitigation mitigation;
        
        try
        {
            mitigation = _mitigations.GetByRiskId(id);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The risk: {Id} has no mitigations: {ExMessage}", id, ex.Message);
            return this.NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting mitigation: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(mitigation);
    }
    
    /// <summary>
    /// Gets a risk score 
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <returns></returns>
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("{id}/Scoring")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<RiskScoring> GetRiskScoring(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got risk scoring with id={Id}", user.Value, id);

        RiskScoring scoring;
        
        try
        {
            scoring = _risksService.GetRiskScoring(id);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The riskscoring: {Id} was not found in the database: {ExMessage}", id, ex.Message);
            return this.NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting riskscoring: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(scoring);
    }
    
    /// <summary>
    /// Gets a risk entity 
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <returns></returns>
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("{id}/Entity")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Int32> GetRiskEntity(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got the entity for risk with id={Id}", user.Value, id);

        Entity entity;
        
        try
        {
            entity = _risksService.GetRiskEntityByRiskId(id);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The entity for risk: {Id} was not found in the database: {ExMessage}", id, ex.Message);
            return this.NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting risk entity: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(entity.Id);
    }
    
    /// <summary>
    /// Updates a risk Entity association
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <param name="entityId">Entity Id</param>
    /// <returns></returns>
    [HttpPut]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("{id}/Entity")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<string> AssociateRiskEntity(int id, [FromBody] int entityId)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} updatet de risk with id={Id}, with entity={EntityId}", 
            user.Value, id, entityId);

        try
        {
            _risksService.CleanRiskEntityAssociations(id);
            _risksService.AssociateRiskWithEntity(id, entityId);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The entity for risk: {Id} was not found in the database: {ExMessage}", id, ex.Message);
            return this.NotFound("Risk or entity not found");
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting risk entity: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok("Entity associated");
    }
    
    /// <summary>
    /// Deletes a risk Entity association
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <param name="entityId">Entity Id</param>
    /// <returns></returns>
    [HttpDelete]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("{id}/Entity")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<string> DeleteRiskEntity(int id, [FromBody] int entityId)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} unassociated de risk with id={Id}, with entity={EntityId}", 
            user.Value, id, entityId);

        try
        {
            _risksService.DeleteEntityAssociation(id, entityId);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The entity for risk: {Id} was not found in the database: {ExMessage}", id, ex.Message);
            return this.NotFound("Risk or entity not found");
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting risk entity: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok("Entity unassociated");
    }
    
    /// <summary>
    /// Gets files associated to a risk
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <returns></returns>
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("{id}/Files")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<FileListing>> GetRiskFiles(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got risk files with id={Id}", user.Value, id);

        try
        {
            var files = _filesService.GetRiskFiles(id);
            return Ok(files);
        }

        catch (Exception ex)
        {
            Logger.Error("Internal error getting risk files: {Message}", ex.Message);
            return StatusCode(500);
        }

        
    }
    
    
    /// <summary>
    /// Gets reviews associated to a risk
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <returns></returns>
    [HttpGet]
    [Route("{id}/MgmtReviews")]
    [Authorize(Policy = "RequireMgmtReviewAccess")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<MgmtReview>> GetRiskMgmtReviews(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got risk reviews with id={Id}", user.Value, id);

        try
        {
            var reviews = _mgmtReviewsService.GetRiskReviews(id);
            return Ok(reviews);
        }

        catch (Exception ex)
        {
            Logger.Error("Internal error getting risk reviews: {Message}", ex.Message);
            return StatusCode(500);
        }

        
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("{id}/Vulnerabilities")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Vulnerability>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Vulnerability>> GetVulnerabilities(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got risk Vulnerabilities with id={Id}", user.Value, id);

        try
        {
            var vulnerabilities = _risksService.GetVulnerabilities(id);
            return Ok(vulnerabilities);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Error("The risk could not be found: {Message}", ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting risk reviews: {Message}", ex.Message);
            return StatusCode(500);
        }

        
    }
    
    [HttpGet]
    [Route("{id}/LastMgmtReview")]
    [Authorize(Policy = "RequireMgmtReviewAccess")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<MgmtReview> GetRiskLastMgmtReview(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got last risk review with id={Id}", user.Value, id);

        try
        {
            var review = _mgmtReviewsService.GetRiskLastReview(id);
            if (review == null) return NotFound();
            return Ok(review);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Error("Risk not found: {Message}", ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting last risk review: {Message}", ex.Message);
            return StatusCode(500);
        }

        
    }
    
    /// <summary>
    /// Gets the review Level for a risk
    /// </summary>
    /// <param name="id">Risk Id</param>
    /// <returns></returns>
    [HttpGet]
    [Route("{id}/ReviewLevel")]
    [Authorize(Policy = "RequireMgmtReviewAccess")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<ReviewLevel> GetRiskReviewLevel(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got risk review level with id={Id}", user.Value, id);

        try
        {
            var rl = _mgmtReviewsService.GetRiskReviewLevel(id);
            return Ok(rl);
        }

        catch (Exception ex)
        {
            Logger.Error("Internal error getting risk review level: {Message}", ex.Message);
            return StatusCode(500);
        }

        
    }
    

    /// <summary>
    /// Creates a new scoring
    /// </summary>
    /// <param name="id">Risk ID</param>
    /// <param name="scoring">Scoring to be created</param>
    /// <returns></returns>
    [HttpPost]
    [Authorize(Policy = "RequireSubmitRisk")]
    [Route("{id}/Scoring")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RiskScoring> CreateRiskScoring(int id, [FromBody] RiskScoring? scoring)
    {
        if(scoring == null) return StatusCode(StatusCodes.Status500InternalServerError);
        
        var user = GetUser();
        var risk = _risksService.GetUserRisk(user, id);
        
        //if(risk == null) return NotFound();

        scoring.Id = risk.Id;

        try
        {
            var finalScoring = _risksService.CreateRiskScoring(scoring);

            return Created($"{id}/Scoring", finalScoring);
        }
        catch (DataAlreadyExistsException daEx)
        {
            return StatusCode(StatusCodes.Status409Conflict, daEx.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }


    }
    
    
    /// <summary>
    /// Get the list of possible risk close reasons
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("CloseReasons")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<CloseReason>> ListCloseReasons()
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} listed risk close reasons", user.Value);
        try
        {
            var reasons = _risksService.GetRiskCloseReasons();
            return Ok(reasons);
        } catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("{riskId}/Closure")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<CloseReason>> GetClosure(int riskId)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} got risk:{Id} closure", user.Value, riskId);
        try
        {
            var closure = _risksService.GetRiskClosureByRiskId(riskId);
            return Ok(closure);
        }
        catch (DataNotFoundException dnfe)
        {
            return NotFound($"Risk or closure not found:{dnfe.Message}");
        } 
        
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }
    
   

    [HttpDelete]
    [Route("{riskId}/Closure")]
    [Authorize(Policy = "RequireCloseRisk")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult ReopenRisk(int riskId)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} reopened risk:{Id}", 
            user.Value, riskId);
        try
        {
            // Let´s check if the risk exists

            var risk = _risksService.GetRisk(riskId);
            
            // let´s check the risk status
            
            if (risk.Status != RiskHelper.GetRiskStatusName(RiskStatus.Closed)) return BadRequest("Risk is not closed");
            
            if (_risksService.ClosureExists(riskId))
            {
                _risksService.DeleteRiskClosure(riskId);
            }

            risk.Status = RiskHelper.GetRiskStatusName(RiskStatus.MitigationPlanned);
            
            _risksService.SaveRisk(risk);

            return Ok();
        }
        catch (DataNotFoundException dnfe)
        {
            return NotFound($"Risk not found:{dnfe.Message}");
        } 
        
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    [HttpPost]
    [Route("{riskId}/Closure")]
    [Authorize(Policy = "RequireCloseRisk")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Closure> CloseRisk(int riskId, [FromBody] Closure closure)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} closed risk:{Id} closure:{Reason}", 
            user.Value, riskId, closure.CloseReason);
        try
        {
            // Let´s check if the risk exists
            var risk = _risksService.GetRisk(riskId);
            
            //if(risk == null) return NotFound($"Risk:{riskId} not found");
            
            if(closure.RiskId != riskId) return BadRequest("RiskId does not match the riskId in the closure");

            //let´s check if the risk is already closed
            if(risk.Status == RiskHelper.GetRiskStatusName(RiskStatus.Closed)) return BadRequest("Risk is already closed");
            
            risk.Status = RiskHelper.GetRiskStatusName(RiskStatus.Closed);

            //let´s check if there is already a closure for this risk
            if (_risksService.ClosureExists(riskId))
            {
                _risksService.DeleteRiskClosure(riskId);
            }

            var newClosure = _risksService.CreateRiskClosure(closure);
            
            risk.CloseId = newClosure.Id;
            _risksService.SaveRisk(risk);
            
            
            return Ok(newClosure);
        }
        catch (DataNotFoundException dnfe)
        {
            return NotFound($"Risk not found:{dnfe.Message}");
        } 
        
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }
    
    
    // Updates a Scoring
    [HttpPut]
    [Route("{id}/Scoring")]
    [Authorize(Policy = "RequireSubmitRisk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult SaveRiskScoring(int id, [FromBody] RiskScoring? scoring = null)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} saved a risk soring:{Id}", user.Value, id);

        if(scoring == null) return StatusCode(StatusCodes.Status500InternalServerError);
            scoring.Id = id;
        try
        {
            _risksService.SaveRiskScoring(scoring);
            return Ok();
        } catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }

    }
    
    // Deletes a Risk scoring
    [HttpDelete]
    [Route("{id}/Scoring")]
    [Authorize(Policy = "RequireDeleteRisk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult DeleteScoring(int id)
    {

        var user = GetUser();
        Logger.Information("User:{UserValue} deleted risk soring:{Id}", user.Value, id);

        try
        {
           
            _risksService.DeleteRiskScoring(id);

            return Ok();
        }
        catch (Exception ex)
        {

            if (typeof(DataNotFoundException) == ex.GetType())
            {
                Logger.Warning("The risk scoring: {Id} was not found in the database:{ExMessage}", id, ex.Message);
                return this.NotFound();
            }
            else
            {
                Logger.Error("Internal error deleting risk scoring");
                return StatusCode(500);
            }

        }
        
        
    }

    // Create new Risk
    [HttpPost]
    [Route("")]
    [Authorize(Policy = "RequireSubmitRisk")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Risk))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Risk> Create([FromBody] Risk? risk = null)
    {

        if(risk == null) return StatusCode(StatusCodes.Status500InternalServerError);
        
        var user = GetUser();

        Logger.Information("User:{UserValue} submitted a risk", user.Value);

        try
        {
            risk.SubmittedBy = user.Value;
            risk.Status = "New";
            
            var crisk = _risksService.CreateRisk(risk);

            if (crisk != null) return Created("risks/" + crisk.Id, crisk);
            
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to create risks message: {ExMessage}", user.Name, ex.Message);
            return this.Unauthorized();
        }
        
        
    }
    
    // Updates a Risk
    [HttpPut]
    [Route("{id}")]
    [Authorize(Policy = "RequireSubmitRisk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult Save(int id, [FromBody] Risk? risk = null)
    {

        if(risk == null) return StatusCode(StatusCodes.Status500InternalServerError);
        
        var user = GetUser();

        Logger.Information("User:{UserValue} updated risk: {Id}", user.Value, id);

        try
        {
            risk.LastUpdate = DateTime.Now;
            
            _risksService.SaveRisk(risk);

            return Ok();
            
            //return StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            if (typeof(UserNotAuthorizedException) == ex.GetType())
            {
                Logger.Warning("The user {UserName} is not authorized to create risks message: {ExMessage}", user.Name, ex.Message);
                return this.Unauthorized();
            }
            else
            {
                Logger.Error("Internal error saving risk");
                return StatusCode(500);
            }

        }
        
        
    }
    
    // Deletes a Risk
    [HttpDelete]
    [Route("{id}")]
    [Authorize(Policy = "RequireDeleteRisk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult Delete(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} deleted risk: {Id}", user.Value, id);

        try
        {
           
            _risksService.DeleteRisk(id);

            return Ok();
        }
        catch (Exception ex)
        {

            if (typeof(DataNotFoundException) == ex.GetType())
            {
                Logger.Warning("The risk: {Id} was not found in the database: {ExMessage}", id, ex.Message);
                return this.NotFound();
            }
            else
            {
                Logger.Error("Internal error deleting risk");
                return StatusCode(500);
            }

        }
        
        
    }
    
    // Check if risk subject exists new Risk
    [HttpGet]
    [Route("Exists")]
    [Authorize(Policy = "RequireSubmitRisk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<bool> Create([FromQuery] string? subject = null)
    {

        try
        {
            if (subject != null)
            {
                var exists = _risksService.SubjectExists(subject);
                if (exists) return StatusCode(StatusCodes.Status200OK);
                return StatusCode(StatusCodes.Status404NotFound);
            }
            

            return StatusCode(StatusCodes.Status500InternalServerError);
            
        }
        catch (UserNotAuthorizedException ex)
        {
            var user = GetUser();
            Logger.Warning("The user {UserValue} is not authorized to check risks subjects message: {ExMessage}", user.Value, ex.Message);
            return this.Unauthorized();
        }
        
        
    }
    

    [HttpGet]
    [Authorize(Policy = "RequireMgmtReviewAccess")]
    [Route("ManagementReviews")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<MgmtReview>> GetAllManagementReviews([FromQuery] string? status = null)
    {
        
        var user = GetUser();

        Logger.Information("User:{UserValue} listed all management reviews", user.Value);
        
        var reviews = new List<MgmtReview>();
        return reviews;
    }

    [HttpGet]
    [Authorize(Policy = "RequireValidUser")]
    [Route("MyRisks")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Risk>> GetMyRisks([FromQuery] string? status = null)
    {
        var user = GetUser();

        Logger.Information("User:{UserValue} listed own risks", user.Value);
        //var risks = new List<Risk>();

        try
        {
            var risks = _risksService.GetUserRisks(user, status);

            if(risks.Count > 0) return Ok(risks);
            return NotFound(risks);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to see risks message: {ExMessage}", user.Name, ex.Message);
            return this.Unauthorized();
        }
        
    }

    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("Categories/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Category> GetRiskCategory(int id)
    {
        
        try
        {
            var cat  = _risksService.GetRiskCategory(id);
            return Ok(cat);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The category {Id} was not found: {ExMessage}", id, ex.Message);
            return NotFound();

        }
        
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("Categories")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Category>> GetRiskCategories()
    {
        
        try
        {
            var cats  = _risksService.GetRiskCategories();
            return Ok(cats);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error Listing categories {ExMessage}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);

        }
        
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("Probabilities")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Likelihood>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Likelihood>> GetRiskProbabilities()
    {
        
        try
        {
            var probs = _risksService.GetRiskProbabilities();
            return Ok(probs);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error Listing probabilities {ExMessage}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);

        }
        
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("Impacts")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Impact>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Impact>> GetRiskImpacts()
    {
        
        try
        {
            var impacts = _risksService.GetRiskImpacts();
            return Ok(impacts);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error Listing impacts {ExMessage}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);

        }
        
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("ScoreValue-{probability}-{impact}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Impact>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<double> GetRiskScoreValue(int probability, int impact)
    {
        
        try
        {
            var score = _risksService.GetRiskScore(probability, impact);
            return Ok(score);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error getting score {ExMessage}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);

        }
        
    }

    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("Catalogs/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<RiskCatalog> GetRiskCatalog(int id)
    {
        
        try
        {
            var cat  = _risksService.GetRiskCatalog(id);
            return Ok(cat);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The catalog {Id} was not found: {ExMessage}", id, ex.Message);
            return NotFound();

        }
        
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("Catalogs")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<RiskCatalog>> GetRisksCatalog([FromQuery] string list = "")
    {
        var all = list == "";

        const string regex = @"^\d+(,\d+)*$";
        var match = Regex.Match(list, regex, RegexOptions.IgnoreCase);

        if (all == false && !match.Success)
        {
            Logger.Warning($"Invalid catalog list format");
            return StatusCode(409);
        }
        
        try
        {
            if (all)
            {
                var cats  = _risksService.GetRiskCatalogs();
                return Ok(cats);
            }
            
            var sids = list.Split(',').ToList();

            var ids = new List<int>();

            foreach (var sid in sids)
            {
                ids.Add(Int32.Parse(sid));
            }
            
            var cat  = _risksService.GetRiskCatalogs(ids);
            return Ok(cat);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The catalogs {List} was not found: {ExMessage}", list, ex.Message);
            return NotFound();

        }
        
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("Sources/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Source> GetRiskSource(int id)
    {
        
        try
        {
            var src  = _risksService.GetRiskSource(id);
            return Ok(src);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The category {Id} was not found: {ExMessage}", id, ex.Message);
            return NotFound();

        }
        
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("Sources")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Source> GetRiskSources()
    {
        
        try
        {
            var srcs  = _risksService.GetRiskSources();
            return Ok(srcs);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error listing the sources: {ExMessage}", ex.Message);
            return NotFound();

        }
        
    }

    [HttpGet]
    [Authorize(Policy = "RequireRiskmanagement")]
    [Route("NeedingMgmtReviews")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Risk>> GetRisksNeedingMgmtReviews([FromQuery] string? status = null)
    {
        var user = GetUser();

        //var risks = new List<Risk>();
        try
        {
            var risks = _risksService.GetRisksNeedingReview(status);

            return Ok(risks);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to see risks message: {ExMessage}", user.Name, ex.Message);
            return this.Unauthorized();
        }

    }
    
}