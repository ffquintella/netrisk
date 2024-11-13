using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq.Expressions;
using System.Text;
using AutoMapper;
using DAL;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.DTO.Statistics;
using Model.Statistics;
using System.Linq;
using ServerServices;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class StatisticsController : ApiBaseController
{
    private readonly IDalService _dalService;
    private readonly IStatisticsService _statisticsService;
    
    private IMapper _mapper;
    public StatisticsController(ILogger logger, IDalService dalService, IMapper mapper,
        IHttpContextAccessor httpContextAccessor, IStatisticsService statisticsService,
        IUsersService usersService) : base(logger, httpContextAccessor, usersService)
    {
        _dalService = dalService;
        _mapper = mapper;
        _statisticsService = statisticsService;
        
    }
    

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<string>> ListAvailable()
    {
        Logger.Debug("Listing available statistics");
        return new List<string> { "RisksOverTime", "SecurityControls", "RisksVsCosts", "Vulnerabilities" };
    }
    
    [HttpGet]
    [Route("Vulnerabilities")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<string>> ListAvailableVulnerabilities()
    {
        Logger.Debug("Listing available vulnerabilities statistics");
        return new List<string> { "Distribution", "VerifiedPercentage", "Numbers", "Sources" };
    }

    [HttpGet] 
    [Route("Vulnerabilities/Distribution")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<ValueName>> VulnerabilitiesDistribution()
    {
        try
        {
            return Ok(_statisticsService.GetVulnerabilitiesDistribution());
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting vulnerabilities distribution");
            return StatusCode(500, e.Message);
        }
    }
    
    [HttpGet] 
    [Route("Vulnerabilities/VerifiedPercentage")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<float> VulnerabilitiesVerifiedPercentage()
    {
        try
        {
            return Ok(_statisticsService.GetVulnerabilitiesVerifiedPercentage());
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting vulnerabilities verified percentage");
            return StatusCode(500, e.Message);
        }
    }
    
    [HttpGet] 
    [Route("Vulnerabilities/Numbers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<VulnerabilityNumbers> VulnerabilitiesNumbers()
    {
        try
        {
            return Ok(_statisticsService.GetVulnerabilityNumbers());
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting vulnerabilities numbers");
            return StatusCode(500, e.Message);
        }
    }
    
    
    [HttpGet] 
    [Route("Vulnerabilities/NumbersByStatus")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<VulnerabilityNumbersByStatus> VulnerabilitiesNumbersByStatus()
    {
        try
        {
            return Ok(_statisticsService.GetVulnerabilitiesNumbersByStatus());
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting vulnerabilities numbers by status");
            return StatusCode(500, e.Message);
        }
    }
    
    [HttpGet] 
    [Route("Vulnerabilities/NumbersByTime")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public async Task<ActionResult<VulnerabilityNumbersByTime>> VulnerabilitiesNumbersByTime()
    {
        try
        {
            return Ok(await _statisticsService.GetVulnerabilitiesNumbersByTimeAsync());
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting vulnerabilities numbers by time");
            return StatusCode(500, e.Message);
        }
    }

    [HttpGet] 
    [Route("Vulnerabilities/Sources")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<ValueName>> VulnerabilitiesSources()
    {
        try
        {
            return Ok(_statisticsService.GetVulnerabilitySources());
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting vulnerabilities sources");
            return StatusCode(500, e.Message);
        }
    }
    

    [HttpGet] 
    [Route("RisksVsCosts")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<LabeledPoints>> RisksVsCosts([FromQuery] double maxRisk = 10, [FromQuery] double minRisk = 0)
    {
        try
        {
            return Ok(_statisticsService.GetRisksVsCosts(minRisk, maxRisk));
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting risks vs costs");
            return StatusCode(500, e.Message);
        }
    }

    [HttpGet] 
    [Route("RisksImpactVsProbability")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<LabeledPoints>> RisksImpactVsProbability([FromQuery] double maxRisk = 10, [FromQuery] double minRisk = 0)
    {
        try
        {
            return Ok(_statisticsService.GetRisksImpactVsProbability(minRisk, maxRisk));
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting risks vs costs");
            return StatusCode(500, e.Message);
        }
    }
    
    [HttpGet] 
    [Route("EntitiesRiskValues")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<ValueNameType>> EntitiesRiskValues([FromQuery] int? parentId = null, [FromQuery] int topCount = 10)
    {
        try
        {
            return Ok(_statisticsService.GetEntitiesRiskValues(parentId, topCount));
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting Entities Risk Values");
            return StatusCode(500, e.Message);
        }
    }
    
    [HttpGet]
    [Route("RisksOverTime")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<RisksOnDay>> GetRisksOverTime([FromQuery]int daysSpan = 30)
    {

        try
        {
            return Ok(_statisticsService.GetRisksOverTime(daysSpan));
            
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting risks overtime");
            return StatusCode(500, e.Message);
        }

    }
    
    [HttpGet]
    [Route("VulnerabilitiesSeverityByImport")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public async Task<ActionResult<List<RisksOnDay>>> GetVulnerabilitiesServerityByImport([FromQuery]int itemCount = 90)
    {

        try
        {
            return Ok(await _statisticsService.GetVulnerabilitiesServerityByImportAsync(itemCount));
            
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting vulnerabilities severity by import");
            return StatusCode(500, e.Message);
        }

    }
    
    [HttpGet] 
    [Route("Risks/Numbers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public async Task<ActionResult<RisksNumbers>> RisksNumbers()
    {
        try
        {
            return Ok(await _statisticsService.GetRisksNumbersAsync());
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting risks numbers");
            return StatusCode(500, e.Message);
        }
    }
    
    [HttpGet] 
    [Route("Risks/Top")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public async Task<ActionResult<List<TopRisk>>> RisksTop([FromQuery] int count = 10)
    {
        try
        {
            return Ok(await _statisticsService.GetRisksTopAsync(count));
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting risks numbers");
            return StatusCode(500, e.Message);
        }
    }
    
    [HttpGet] 
    [Route("Risks/TopGroups")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public async Task<ActionResult<List<RiskGroup>>> RisksTopGroups()
    {
        try
        {
            return Ok(await _statisticsService.GetRisksTopGroupsAsync());
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting risks numbers");
            return StatusCode(500, e.Message);
        }
    }

    [HttpGet]
    [Route("Risks/TopEntities")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public async Task<ActionResult<List<RiskEntity>>> RisksTopEntities([FromQuery] int count = 10, [FromQuery] string? entityType = null)
    {
        try
        {
            return Ok(await _statisticsService.GetRisksTopEntities(count, entityType));
        }catch(Exception e)
        {
            Logger.Error(e, "Error while getting risks numbers");
            return StatusCode(500, e.Message);
        }
    }
    
    [HttpGet]
    [Route("SecurityControls")]
    public ActionResult<SecurityControlsStatistics?> GetSecurityControls()
    {
      
        
        var srDbContext = _dalService.GetContext();
       
        var risks = srDbContext.Risks.Join(srDbContext.RiskScorings, 
            risk => risk.Id,
            riskScoring => riskScoring.Id,
            (risk, riskScoring) => new
            {
                Id = risk.Id,
                SubmissionDate = risk.SubmissionDate,
                CalculatedRisk = riskScoring.CalculatedRisk,
                Status = risk.Status,
                ControlNumber = risk.ControlNumber
            }).Where(risk => risk.Status != "Closed").ToList();
        
        //risk.RiskCatalogMappings.Split(',').Select(int.Parse)
        
        var dbControls = srDbContext.Frameworks.Join(srDbContext.FrameworkControlMappings, 
                framework => framework.Value,
                frameworkControlMappings => frameworkControlMappings.Framework,
                (framework, frameworkControlMappings) => new
                {
                    Framework = framework.Name,
                    FrameworkId = framework.Value,
                    ControlId = frameworkControlMappings.ControlId,
                    ReferemceName = frameworkControlMappings.ReferenceName,
                }).Join(srDbContext.FrameworkControls,
                    frameworkControlMappings => frameworkControlMappings.ControlId,
                    frameworkControls => frameworkControls.Id,
                    (framework,  frameworkControls) => new
                    {
                        Framework = Encoding.UTF8.GetString(framework.Framework),
                        FrameworkId = framework.FrameworkId,
                        ControlId = framework.ControlId,
                        ReferemceName = framework.ReferemceName,
                        ControlName = frameworkControls.ShortName,
                        ClassId = frameworkControls.ControlClass,
                        MaturityId = frameworkControls.ControlMaturity,
                        DesireedMaturityId = frameworkControls.DesiredMaturity,
                        PiorityId = frameworkControls.ControlPriority,
                        Status = frameworkControls.Status,
                        Deleted = frameworkControls.Deleted,
                        ControlNumber = frameworkControls.ControlNumber,
                    }
                ).Where(sc => sc.Status == 1 && sc.Deleted == 0).ToList();


        var dbControlRisks = dbControls.Where(fc => fc.ControlNumber != null 
                                                    && fc.ControlNumber != "")
            .GroupBy(cr => cr.ControlId)
            .Select(fc => new SecurityControlStatistic
        {
            TotalRisk = risks.Where(r => r.ControlNumber == fc.FirstOrDefault()!.ControlNumber).Select(risk => risk.CalculatedRisk).Sum(),
            Framework = fc.FirstOrDefault()!.Framework,
            FrameworkId = fc.FirstOrDefault()!.FrameworkId,
            ControlId = fc.FirstOrDefault()!.ControlId,
            ReferemceName = fc.FirstOrDefault()!.ReferemceName,
            ControlName = fc.FirstOrDefault()!.ControlName!,
            ClassId = fc.FirstOrDefault()!.ClassId,
            MaturityId = fc.FirstOrDefault()!.MaturityId,
            DesireedMaturityId = fc.FirstOrDefault()!.DesireedMaturityId,
            PiorityId = fc.FirstOrDefault()!.PiorityId,
            Status = fc.FirstOrDefault()!.Status,
            Deleted = fc.FirstOrDefault()!.Deleted,
            ControlNumber = fc.FirstOrDefault()!.ControlNumber!,
        }).OrderBy(sc => sc.TotalRisk).ToList();
        
        var frameworkStats = dbControls
            .GroupBy(dc => dc.FrameworkId)
            .Select(st => new FrameworkStatistic()
        {
            Framework = st.First().Framework,
            Count = st.Count(),
            TotalMaturity = st.Sum(m => m.MaturityId),
            TotalDesiredMaturity = st.Sum(m => m.DesireedMaturityId),
        }).ToList();
        
        var result = new SecurityControlsStatistics
        {
            SecurityControls = dbControlRisks,
            FameworkStats = frameworkStats 
        };
        
        return result;

    }
    
}