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
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class Statistics : ApiBaseController
{
    private readonly DALManager _dalManager;
    
    private IMapper _mapper;
    public Statistics(ILogger logger, DALManager dalManager, IMapper mapper,
        IHttpContextAccessor httpContextAccessor,
        IUserManagementService userManagementService) : base(logger, httpContextAccessor, userManagementService)
    {
        _dalManager = dalManager;
        _mapper = mapper;
    }
    

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<string>> ListAvaliable()
    {
        Logger.Information("Listing avaliable statistics");
        return new List<string> { "RisksOverTime", "SecurityControls" };
    }
    
    [HttpGet]
    [Route("RisksOverTime")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<RisksOnDay>> GetRisksOverTime([FromQuery]int daysSpan = 30)
    {

        var firstDay = DateTime.Now.Subtract(TimeSpan.FromDays(daysSpan));
        
        var srDbContext = _dalManager.GetContext();
        var risks = srDbContext.Risks.Join(srDbContext.RiskScorings, 
            risk => risk.Id,
            riskScoring => riskScoring.Id,
            (risk, riskScoring) => new
            {
                Id = risk.Id,
                SubmissionDate = risk.SubmissionDate,
                CalculatedRisk = riskScoring.CalculatedRisk,
                Status = risk.Status
            }).
            Where(risk => risk.SubmissionDate > firstDay).ToList();
        
        var result = new List<RisksOnDay>();

        var computingDay = firstDay;

        while (computingDay < DateTime.Now)
        {
            var risksSelected = risks.Where(rsk => rsk.SubmissionDate.Date == computingDay.Date && rsk.Status != "Closed").ToList();

            var riskOnDay = new RisksOnDay
            {
                Day = computingDay,
                RisksCreated = 0,
                TotalRiskValue = 0
            };
            
            foreach (var risk in risksSelected)
            {
                riskOnDay.RisksCreated++;
                riskOnDay.TotalRiskValue += risk.CalculatedRisk;
            }
            result.Add(riskOnDay);
            computingDay = computingDay.AddDays(1);
        }

        return result;

    }
    
    [HttpGet]
    [Route("SecurityControls")]
    public ActionResult<SecurityControlsStatistics?> GetSecurityControls()
    {
      
        
        var srDbContext = _dalManager.GetContext();
       
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