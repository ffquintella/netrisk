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
public class ReportsController: ApiBaseController
{
    private IReportsService ReportsService { get; }
    public ReportsController(IReportsService reportsService, ILogger logger, IHttpContextAccessor httpContextAccessor, IUsersService usersService) : base(logger, httpContextAccessor, usersService)
    {
        ReportsService = reportsService;
    }
    
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public ActionResult<List<string>> Get()
    {
        var user = GetUser();

        Logger.Information("User:{UserValue} listed reports", user.Value);
        return Ok(ReportsService.GetAll().ToList());
    }
}