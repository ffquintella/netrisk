using AutoMapper;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using Sieve.Models;
using Sieve.Services;
using Tools.Helpers;

namespace ServerServices.Services;

public class VulnerabilitiesService(
    ILogger logger,
    IDalService dalService,
    IMapper mapper,
    ISieveProcessor sieveProcessor)
    : ServiceBase(logger, dalService), IVulnerabilitiesService
{
    private IMapper Mapper { get; } = mapper;

    private ISieveProcessor SieveProcessor { get; } = sieveProcessor;

    public List<Vulnerability> GetAll()
    {
        using var dbContext = DalService.GetContext();
        
        List<Vulnerability> vulnerabilities = dbContext.Vulnerabilities
            .Include(vul => vul.Risks).ToList();
        
        return vulnerabilities;
    }

    public List<Vulnerability> GetFiltred(SieveModel sieveModel, out int totalCount, bool includeFixRequests = false)
    {
        using var dbContext = DalService.GetContext();

        var vul = dbContext.Vulnerabilities;

        IQueryable<Vulnerability> result;
        
        if(includeFixRequests) result = vul.Include(v => v.Risks).Include(v => v.FixRequests).AsNoTracking();
        else result = vul.Include(v => v.Risks).AsNoTracking();
        
        //var result = dbContext.Vulnerabilities
        //    .Include(vul => vul.Risks).Include(vul => vul.FixRequests).AsNoTracking();
        
        //if(includeFixRequests) result = result.Include(vul => vul.FixRequests);
        
        //result = result.AsNoTracking(); // Makes read-only queries faster
         
        var vulnerabilities = SieveProcessor.Apply(sieveModel, result, applyPagination: false);
        totalCount = vulnerabilities.Count();
        
        result = SieveProcessor.Apply(sieveModel, result); // Returns `result` after applying the sort/filter/page query in `SieveModel` to it
        return result.ToList();
    }

    public Vulnerability GetById(int vulnerabilityId, bool includeDetails = false)
    {
        return AsyncHelper.RunSync(() => GetByIdAsync(vulnerabilityId, includeDetails));
    }

    public async Task<Vulnerability> GetByIdAsync(int vulnerabilityId, bool includeDetails = false)
    {
        await using var dbContext = DalService.GetContext();

        Vulnerability? vulnerability;
        
        if(!includeDetails) vulnerability = await dbContext.Vulnerabilities.FindAsync(vulnerabilityId);
        else
        {
            vulnerability = await dbContext.Vulnerabilities
                .AsNoTrackingWithIdentityResolution()
                .Include(vul => vul.FixTeam)
                .Include(vul => vul.Host)
                .Include(vul => vul.Actions.OrderByDescending(a => a.DateTime))
                .Include(vul => vul.Risks).ThenInclude(risk => risk.CategoryNavigation)
                .Include(vul => vul.Risks).ThenInclude(r => r.SourceNavigation)
                .FirstOrDefaultAsync(vul => vulnerabilityId == vul.Id);
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

    public async Task<Vulnerability> CreateAsync(Vulnerability vulnerability)
    {
        vulnerability.Id = 0;
        await using var dbContext = DalService.GetContext();

        var newVulnerability = dbContext.Vulnerabilities.Add(vulnerability);
        await dbContext.SaveChangesAsync();
        
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

        vulnerability.Actions = actions;
        
        Mapper.Map(vulnerability, dbVulnerability);
        
        dbContext.SaveChanges();
    }

    public async Task UpdateAsync(Vulnerability vulnerability)
    {
        
        try
        {
            if (vulnerability == null) throw new ArgumentNullException(nameof(vulnerability));
            if (vulnerability.Id == 0) throw new ArgumentException("Vulnerability id cannot be 0");

            await using var dbContext = DalService.GetContext();
            var dbVulnerability = await dbContext.Vulnerabilities
                .FirstOrDefaultAsync(vul => vul.Id == vulnerability.Id);

            if (dbVulnerability == null)
                throw new DataNotFoundException("vulnerabilities", vulnerability.Id.ToString(),
                    new Exception("Vulnerability not found"));
                
            // Detach the passed in vulnerability instance
            dbContext.Entry(vulnerability).State = EntityState.Detached;

            foreach (var action in vulnerability.Actions)
            {
                action.Vulnerabilities = null!;
            }

            vulnerability.Host = null;
            vulnerability.FixTeam = null;

            foreach (var risk in vulnerability.Risks)
            {
                risk.Vulnerabilities = null!;
            }
                
            Mapper.Map(vulnerability, dbVulnerability);
                
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Error on update operation message: {Message}", ex.Message);
            if (ex.InnerException != null)
            {
                Log.Error("Error on update operation inner exception message: {Message}", ex.InnerException.Message);
            }
        }

    }

    public void AssociateRisks(int id, List<int> riskIds)
    {
        using var dbContext = DalService.GetContext();
        
        var risks = dbContext.Risks.Where(r => riskIds.Contains(r.Id)).ToList();
        
        var vulnerability = dbContext.Vulnerabilities.Include(v=>v.Risks).FirstOrDefault(v => v.Id == id);
        
        if( vulnerability == null) throw new DataNotFoundException("vulnerabilities",id.ToString(),
            new Exception("Vulnerability not found"));


        vulnerability.Risks.Clear();
        
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
            .AsNoTrackingWithIdentityResolution()
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

    public async Task<Vulnerability?> FindAsync(string hash)
    {
        Vulnerability? vulnerability = null;
        using (var dbContext = DalService.GetContext())
        { 
            vulnerability = await dbContext.Vulnerabilities
                .AsNoTrackingWithIdentityResolution()
                .Include(vul => vul.FixTeam)
                .Include(vul => vul.Host)
                .Include(vul => vul.Actions)
                .Include(vul => vul.Risks).ThenInclude(risk => risk.CategoryNavigation)
                .Include(vul => vul.Risks).ThenInclude(r => r.SourceNavigation)
                .FirstOrDefaultAsync(vul => vul.ImportHash == hash);

            if (vulnerability == null)
                throw new DataNotFoundException("netrisk", nameof(Vulnerability),
                    new Exception("Vulnerability not found"));
            
        }

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

    public async void UpdateCommentsAsync(int id, string comments)
    {
        await using var dbContext = DalService.GetContext();
        var vulnerability = await dbContext.Vulnerabilities.FindAsync(id);
        if(vulnerability == null) throw new DataNotFoundException("vulnerabilities",id.ToString(),
            new Exception("Vulnerability not found"));
        vulnerability.Comments = comments;
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

    public async Task<NrAction> AddActionAsync(int id, int userId, NrAction action)
    {
        await using var dbContext = DalService.GetContext();
        var vulnerability = await dbContext.Vulnerabilities
            .Include(vul => vul.Actions).FirstOrDefaultAsync(vul => vul.Id == id);
            
        if(vulnerability == null) throw new DataNotFoundException("vulnerabilities",id.ToString(),
            new Exception("Vulnerability not found"));
        
        action.UserId = userId;
        action.Id = 0;
        
        vulnerability.Actions.Add(action);
        await dbContext.SaveChangesAsync();
        return action;
    }

    public async Task<List<Vulnerability>> GetVulnerabilitiesByHostIdAsync(int hostId)
    {
        await using var dbContext = DalService.GetContext();
        
        var vulnerabilities = await dbContext.Vulnerabilities
            .Include(vul => vul.Risks)
            .Where(vul => vul.HostId == hostId)
            .ToListAsync();
        if(vulnerabilities == null) throw new DataNotFoundException("vulnerabilities",hostId.ToString(),
            new Exception("Vulnerabilities not found"));
        return vulnerabilities;
    }
    
}