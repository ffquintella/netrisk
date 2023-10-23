﻿using AutoMapper;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using Sieve.Models;
using Sieve.Services;

namespace ServerServices.Services;

public class VulnerabilitiesService: ServiceBase, IVulnerabilitiesService
{
    private IMapper Mapper { get; }
    
    private ISieveProcessor SieveProcessor { get; }
    public VulnerabilitiesService(ILogger logger, DALService dalService, IMapper mapper, ISieveProcessor sieveProcessor) : base(logger, dalService)
    {
        Mapper = mapper;
        SieveProcessor = sieveProcessor;
    }
    
    public List<Vulnerability> GetAll()
    {
        using var dbContext = DalService.GetContext();
        
        List<Vulnerability> vulnerabilities = dbContext.Vulnerabilities.ToList();
        
        return vulnerabilities;
    }

    public List<Vulnerability> GetFiltred(SieveModel sieveModel)
    {
        using var dbContext = DalService.GetContext();
        
        var result = dbContext.Vulnerabilities.AsNoTracking(); // Makes read-only queries faster
        result = SieveProcessor.Apply(sieveModel, result); // Returns `result` after applying the sort/filter/page query in `SieveModel` to it
        return result.ToList();
    }

    public Vulnerability GetById(int vulnerabilityId, bool includeDetails = false)
    {
        using var dbContext = DalService.GetContext();

        Vulnerability? vulnerability;
        
        if(!includeDetails) vulnerability = dbContext.Vulnerabilities.Find(vulnerabilityId);
        else
        {
            vulnerability = dbContext.Vulnerabilities
                .Include(vul => vul.FixTeam)
                .Include(vul => vul.Host)
                .Include(vul => vul.Actions)
                .Include(vul => vul.Risks).ThenInclude(risk => risk.CategoryNavigation)
                .Include(vul => vul.Risks).ThenInclude(r => r.SourceNavigation)
                .FirstOrDefault(vul => vulnerabilityId == vul.Id);
        }
        
        if( vulnerability == null) throw new DataNotFoundException("vulnerabilities",vulnerabilityId.ToString(), 
            new Exception("Vulnerability not found"));
        
        return vulnerability;
    }

    public void Delete(int vulnerabilityId)
    {
        using var dbContext = DalService.GetContext();

        var vulnerability = dbContext.Vulnerabilities.Find(vulnerabilityId);
        
        if( vulnerability == null) throw new DataNotFoundException("vulnerabilities",vulnerabilityId.ToString(),
            new Exception("Vulnerability not found"));
        
        dbContext.Vulnerabilities.Remove(vulnerability);
        dbContext.SaveChanges();
    }

    public Vulnerability Create(Vulnerability vulnerability)
    {
        vulnerability.Id = 0;
        using var dbContext = DalService.GetContext();

        var newVulnerability = dbContext.Vulnerabilities.Add(vulnerability);
        dbContext.SaveChanges();
        
        return newVulnerability.Entity;
    }

    public void Update(Vulnerability vulnerability)
    {
        if(vulnerability == null) throw new ArgumentNullException(nameof(vulnerability));
        if(vulnerability.Id == 0) throw new ArgumentException("Vulnerability id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        
        var dbVulnerability = dbContext.Vulnerabilities.Include(vul => vul.Actions).FirstOrDefault(vul => vul.Id == vulnerability.Id);
        
        if(dbVulnerability == null) throw new DataNotFoundException("vulnerabilities",vulnerability.Id.ToString(),
            new Exception("Vulnerability not found"));
        
        var actions = dbVulnerability.Actions.ToList();
        
        if( dbVulnerability == null) throw new DataNotFoundException("vulnerabilities",vulnerability!.Id.ToString(),
            new Exception("Vulnerability not found"));

        vulnerability.Actions = actions;
        
        Mapper.Map(vulnerability, dbVulnerability);
        
        dbContext.SaveChanges();
    }

    public void AssociateRisks(int id, List<int> riskIds)
    {
        using var dbContext = DalService.GetContext();
        
        var risks = dbContext.Risks.Where(r => riskIds.Contains(r.Id)).ToList();
        
        var vulnerability = dbContext.Vulnerabilities.Include(v=>v.Risks).FirstOrDefault(v => v.Id == id);
        
        if( vulnerability == null) throw new DataNotFoundException("vulnerabilities",id.ToString(),
            new Exception("Vulnerability not found"));


        foreach (var risk in risks)
        {
            if(vulnerability.Risks.Contains(risk)) continue;
            vulnerability.Risks.Add(risk);
        }
        

        dbContext.SaveChanges();
    }
    
    public Vulnerability Find(string hash){
        
        using var dbContext = DalService.GetContext();

        Vulnerability? vulnerability;
        

            vulnerability = dbContext.Vulnerabilities
                .Include(vul => vul.FixTeam)
                .Include(vul => vul.Host)
                .Include(vul => vul.Actions)
                .Include(vul => vul.Risks).ThenInclude(risk => risk.CategoryNavigation)
                .Include(vul => vul.Risks).ThenInclude(r => r.SourceNavigation)
                .FirstOrDefault(vul => vul.ImportHash == hash);
        
        
        if( vulnerability == null) throw new DataNotFoundException("netrisk",nameof(Vulnerability), 
            new Exception("Vulnerability not found"));
        
        return vulnerability;
    }

    public void UpdateStatus(int id, ushort status)
    {
        using var dbContext = DalService.GetContext();
        var vulnerability = dbContext.Vulnerabilities.Find(id);
        if(vulnerability == null) throw new DataNotFoundException("vulnerabilities",id.ToString(),
            new Exception("Vulnerability not found"));
        vulnerability.Status = status;
        dbContext.SaveChanges();
    }
    
    public NrAction AddAction(int id, int userId, NrAction action)
    {
        using var dbContext = DalService.GetContext();
        var vulnerability = dbContext.Vulnerabilities
            .Include(vul => vul.Actions).FirstOrDefault(vul => vul.Id == id);
            
        if(vulnerability == null) throw new DataNotFoundException("vulnerabilities",id.ToString(),
            new Exception("Vulnerability not found"));
        
        action.UserId = userId;
        action.Id = 0;
        
        vulnerability.Actions.Add(action);
        dbContext.SaveChanges();
        return action;
    }
    
}