using System.Text.RegularExpressions;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Risks;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireRiskmanagement")]
[ApiController]
[Route("[controller]")]
public class RisksController : ApiBaseController
{

    private IRiskManagementService _riskManagement;
    private IMitigationManagementService _mitigationManagement;

    public RisksController(
        ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IUserManagementService userManagementService,
        IMitigationManagementService mitigationManagementService,
        IRiskManagementService riskManagement) : base(logger, httpContextAccessor, userManagementService)
    {
        _riskManagement = riskManagement;
        _mitigationManagement = mitigationManagementService;
    }
    
    
    
    
    
    [HttpGet]
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
                risks = _riskManagement.GetAll(status, notStatus:"Closed");
            else 
                risks = _riskManagement.GetAll(status, notStatus:null);

            return Ok(risks);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to see risks message: {ExMessage}", user.Name, ex.Message);
            return this.Unauthorized();
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
                risk = _riskManagement.GetRisk(id);
            }else risk = _riskManagement.GetUserRisk(user, id);
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
            mitigation = _mitigationManagement.GetByRiskId(id);
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
            scoring = _riskManagement.GetRiskScoring(id);
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
    /// Creates a new scoring
    /// </summary>
    /// <param name="id">Risk ID</param>
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
        var risk = _riskManagement.GetUserRisk(user, id);
        
        if(risk == null) return NotFound();

        scoring.Id = risk.Id;

        try
        {
            var final_scoring = _riskManagement.CreateRiskScoring(scoring);

            return Created($"{id}/Scoring", final_scoring);
        }
        catch (DataAlreadyExistsException daex)
        {
            return StatusCode(StatusCodes.Status409Conflict, daex.Message);
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
            var reasons = _riskManagement.GetRiskCloseReasons();
            return Ok(reasons);
        } catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }
    
    [HttpGet]
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
            var closure = _riskManagement.GetRiskClosureByRiskId(riskId);
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
            /// Let´s check if the risk exists

            var risk = _riskManagement.GetRisk(riskId);
            
            /// let´s check the risk status
            
            if (risk.Status != RiskHelper.GetRiskStatusName(RiskStatus.Closed)) return BadRequest("Risk is not closed");
            
            if (_riskManagement.ClosureExists(riskId))
            {
                _riskManagement.DeleteRiskClosure(riskId);
            }

            risk.Status = RiskHelper.GetRiskStatusName(RiskStatus.MitigationPlanned);
            
            _riskManagement.SaveRisk(risk);

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
            /// Let´s check if the risk exists

            var risk = _riskManagement.GetRisk(riskId);
            
            //if(risk == null) return NotFound($"Risk:{riskId} not found");
            
            if(closure.RiskId != riskId) return BadRequest("RiskId does not match the riskId in the closure");

            //let´s check if the risk is already closed
            if(risk.Status == RiskHelper.GetRiskStatusName(RiskStatus.Closed)) return BadRequest("Risk is already closed");
            
            risk.Status = RiskHelper.GetRiskStatusName(RiskStatus.Closed);

            //let´s check if there is already a closure for this risk
            if (_riskManagement.ClosureExists(riskId))
            {
                _riskManagement.DeleteRiskClosure(riskId);
            }

            var newClosure = _riskManagement.CreateRiskClosure(closure);
            
            risk.CloseId = newClosure.Id;
            _riskManagement.SaveRisk(risk);
            
            
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
            _riskManagement.SaveRiskScoring(scoring);
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
           
            _riskManagement.DeleteRiskScoring(id);

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
            
            var crisk = _riskManagement.CreateRisk(risk);

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
            
            _riskManagement.SaveRisk(risk);

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
           
            _riskManagement.DeleteRisk(id);

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
                var exists = _riskManagement.SubjectExists(subject);
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
            var risks = _riskManagement.GetUserRisks(user, status);

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
    [Route("Categories/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Category> GetRiskCategory(int id)
    {
        
        try
        {
            var cat  = _riskManagement.GetRiskCategory(id);
            return Ok(cat);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The category {Id} was not found: {ExMessage}", id, ex.Message);
            return NotFound();

        }
        
    }
    
    [HttpGet]
    [Route("Categories")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Category>> GetRiskCategories()
    {
        
        try
        {
            var cats  = _riskManagement.GetRiskCategories();
            return Ok(cats);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error Listing categories {ExMessage}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);

        }
        
    }
    
    [HttpGet]
    [Route("Probabilities")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Likelihood>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Likelihood>> GetRiskProbabilities()
    {
        
        try
        {
            var probs = _riskManagement.GetRiskProbabilities();
            return Ok(probs);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error Listing probabilities {ExMessage}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);

        }
        
    }
    
    [HttpGet]
    [Route("Impacts")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Impact>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Impact>> GetRiskImpacts()
    {
        
        try
        {
            var impacts = _riskManagement.GetRiskImpacts();
            return Ok(impacts);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error Listing impacts {ExMessage}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);

        }
        
    }
    
    [HttpGet]
    [Route("ScoreValue-{probability}-{impact}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Impact>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<double> GetRiskScoreValue(int probability, int impact)
    {
        
        try
        {
            var score = _riskManagement.GetRiskScore(probability, impact);
            return Ok(score);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error getting score {ExMessage}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);

        }
        
    }

    
    [HttpGet]
    [Route("Catalogs/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<RiskCatalog> GetRiskCatalog(int id)
    {
        
        try
        {
            var cat  = _riskManagement.GetRiskCatalog(id);
            return Ok(cat);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The catalog {Id} was not found: {ExMessage}", id, ex.Message);
            return NotFound();

        }
        
    }
    
    [HttpGet]
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
                var cats  = _riskManagement.GetRiskCatalogs();
                return Ok(cats);
            }
            
            var sids = list.Split(',').ToList();

            var ids = new List<int>();

            foreach (var sid in sids)
            {
                ids.Add(Int32.Parse(sid));
            }
            
            var cat  = _riskManagement.GetRiskCatalogs(ids);
            return Ok(cat);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The catalogs {List} was not found: {ExMessage}", list, ex.Message);
            return NotFound();

        }
        
    }
    
    [HttpGet]
    [Route("Sources/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Source> GetRiskSource(int id)
    {
        
        try
        {
            var src  = _riskManagement.GetRiskSource(id);
            return Ok(src);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The category {Id} was not found: {ExMessage}", id, ex.Message);
            return NotFound();

        }
        
    }
    
    [HttpGet]
    [Route("Sources")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Source> GetRiskSources()
    {
        
        try
        {
            var srcs  = _riskManagement.GetRiskSources();
            return Ok(srcs);
            
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Error listing the sources: {ExMessage}", ex.Message);
            return NotFound();

        }
        
    }

    [HttpGet]
    [Route("NeedingMgmtReviews")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Risk>> GetRisksNeedingMgmtReviews([FromQuery] string? status = null)
    {
        var user = GetUser();

        //var risks = new List<Risk>();
        try
        {
            var risks = _riskManagement.GetRisksNeedingReview(status);

            return Ok(risks);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to see risks message: {ExMessage}", user.Name, ex.Message);
            return this.Unauthorized();
        }

    }
    
}