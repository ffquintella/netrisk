using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mapster;
using DAL;
using DAL.Entities;
using Model.Exceptions;
using Serilog;
using Serilog.Core;
using ServerServices.Interfaces;
using Microsoft.EntityFrameworkCore;
using Model;
using Sieve.Models;
using Sieve.Services;
using Tools.Helpers;

namespace ServerServices.Services;

public class RisksService(
    IDalService dalService,
    IRolesService rolesService,
    ISieveProcessor sieveProcessor,
    IUsersService usersService)
    : IRisksService
{

    
    private ISieveProcessor SieveProcessor { get; } = sieveProcessor;

    /// <summary>
    /// Gets the risks associated to a user
    /// </summary>
    /// <param name="user">The user object</param>
    /// <param name="status">String representing the risk status</param>
    /// <param name="notStatus">String representing the status the risks should not have</param>
    /// <returns></returns>
    /// <exception cref="InvalidParameterException"></exception>
    /// <exception cref="UserNotAuthorizedException"></exception>
    public List<Risk> GetUserRisks(User user, string? status = null, string? notStatus = "Closed")
    {
        if (user == null) throw new InvalidParameterException("user","User cannot be null");
        
        //if (!UserHasRisksPermission(user)) throw new UserNotAuthorizedException(user.Name, user.Value, "risks");
        
        //var risks = new List<Risk>();

        List<Risk> risks;

        //if (user.Admin) return GetAll(status);
        if (user.Admin) return GetAllAsync(status).GetAwaiter().GetResult();
        
        // If the user not an admin we will check if the user has permission to modify risks  if so he can read all 
        //if (UserHasRisksPermission(user, "modify_risks")) return GetAll();
        if (UserHasRisksPermission(user, "modify_risks")) //return AsyncHelper.RunSync<List<Risk>>(GetAllAsync);
            return GetAllAsync().GetAwaiter().GetResult();
        
        // if not he can only see the risks associated to himself or that he created
        using var context = dalService.GetContext();
        if (status != null && notStatus != null)
        {
            risks = context.Risks.Where(r => r.Status == status && r.Status != notStatus
                                                                && (r.Owner == user.Value 
                                                                    || r.SubmittedBy == user.Value
                                                                    || r.Manager == user.Value))
                .Include(r=>r.SourceNavigation)
                .Include(r => r.CategoryNavigation).ToList();
        }
        else if (status != null)
        {
            risks = context.Risks.Where(r => r.Status == status && (r.Owner == user.Value 
                                                                    || r.SubmittedBy == user.Value
                                                                    || r.Manager == user.Value))
                .Include(r=>r.SourceNavigation)
                .Include(r => r.CategoryNavigation).ToList();
        }
        else if (notStatus != null)
        {
            risks = context.Risks.Where(r =>  r.Status != notStatus
                                              && (r.Owner == user.Value 
                                                  || r.SubmittedBy == user.Value
                                                  || r.Manager == user.Value))
                .Include(r=>r.SourceNavigation)
                .Include(r => r.CategoryNavigation).ToList();
        }
        else
        {
            risks = context.Risks.Where(r => r.Owner == user.Value
                                             || r.SubmittedBy == user.Value
                                             || r.Manager == user.Value)
                .Include(r=>r.SourceNavigation)
                .Include(r => r.CategoryNavigation).ToList();
        }

        return risks;
    }


    public Risk GetUserRisk(User user, int id)
    {
        if (user == null) throw new InvalidParameterException("user","User cannot be null");
        if (UserHasRisksPermission(user)) return GetRisk(id);
        else
        {
            var risk = GetRisk(id);
            if(risk.Owner == user.Value || risk.SubmittedBy == user.Value || risk.Manager == user.Value)
                return risk;
            else
                throw new UserNotAuthorizedException(user.Name, user.Value, "risks");
            
        }
    }

    public List<Risk> GetToReview(int daysSinceLastReview, string? status = null, bool includeNew = false)
    {
        var result = new List<Risk>();

        using var context = dalService.GetContext();
        
        //var mgmtReviews = context.MgmtReviews.Where(mr => mr.SubmissionDate.AddDays(daysSinceLastReview) < DateTime.Now).ToList();

        //var risks = context.Risks.Include(r => r.MgmtReviews).ToList();
        
        var risks = context.Risks.Include(r => r.MgmtReviews)
            .Where(r => r.Status != "Closed")
            .Where(r => r.Status != "New")
            .Where(r => r.MgmtReviews.Count > 0)
            .Where(r => r.MgmtReviews.OrderBy(mr => mr.SubmissionDate)
                .LastOrDefault()!.SubmissionDate.AddDays(daysSinceLastReview) < DateTime.Now)
            .Include(r=>r.SourceNavigation)
            .Include(r => r.CategoryNavigation).ToList();
            
        
        return risks;
    }

    public Risk GetRisk(int id)
    {
        using (var context = dalService.GetContext())
        {
            var risk = context.Risks.Include(r=>r.SourceNavigation)
                .Include(r => r.CategoryNavigation).FirstOrDefault(r => r.Id == id);
            if (risk == null)
            {
                Log.Error("Risk with id {Id} not found", id);
                throw new DataNotFoundException("Risk", id.ToString());
            }

            return risk;
        }
    }

    public RiskScoring GetRiskScoring(int id)
    {
        using (var context = dalService.GetContext())
        {
            var scoring = context.RiskScorings.FirstOrDefault(rs => rs.Id == id);
            if (scoring == null)
            {
                Log.Error("Risk Scoring with id {Id} not found", id);
                throw new DataNotFoundException("RiskScoring", id.ToString());
            }

            return scoring;
        }
    }

    public Entity GetRiskEntityByRiskId(int riskId)
    {
        using var context = dalService.GetContext();

        var risk = context.Risks.Include(r => r.Entities)
            .FirstOrDefault(r => r.Id == riskId);
        
        if (risk == null)
        {
            Log.Error("Risk id {Id} was not found", riskId);
            throw new DataNotFoundException("Risk", riskId.ToString());
        }
        
        var entities = risk.Entities;
        
        
        if (entities == null || entities.Count == 0)
        {
            Log.Error("Risk id {Id} has no entity not found", riskId);
            throw new DataNotFoundException("RiskEntities", riskId.ToString());
        }

        return entities!.FirstOrDefault()!;
    }

    public void AssociateRiskWithEntity(int riskId, int entityId)
    {
        using var context = dalService.GetContext();

        var risk = context.Risks.Include(r => r.Entities).FirstOrDefault(r => r.Id == riskId);
        var entity = context.Entities.FirstOrDefault(e => e.Id == entityId);
        
        if (risk == null)
        {
            Log.Error("Risk id {Id} was not found", riskId);
            throw new DataNotFoundException("Risk", riskId.ToString());
        }
        
        if (entity == null)
        {
            Log.Error("Entity id {Id} was not found", riskId);
            throw new DataNotFoundException("Entity", entityId.ToString());
        }
        
        risk.Entities.Add(entity);

        context.SaveChanges();
    }

    public void CleanRiskEntityAssociations(int riskId)
    {
        using var context = dalService.GetContext();

        var risk = context.Risks.Include(r => r.Entities).FirstOrDefault(r => r.Id == riskId);
        
        if (risk == null)
        {
            Log.Error("Risk id {Id} was not found", riskId);
            throw new DataNotFoundException("Risk", riskId.ToString());
        }
        
        risk.Entities.Clear();

        context.SaveChanges(); 
    }

    public void DeleteEntityAssociation(int riskId, int entityId)
    {
        using var context = dalService.GetContext();

        var risk = context.Risks.Include(r => r.Entities).FirstOrDefault(r => r.Id == riskId);
        var entity = context.Entities.FirstOrDefault(e => e.Id == entityId);
        
        if (risk == null)
        {
            Log.Error("Risk id {Id} was not found", riskId);
            throw new DataNotFoundException("Risk", riskId.ToString());
        }
        
        if (entity == null)
        {
            Log.Error("Entity id {Id} was not found", riskId);
            throw new DataNotFoundException("Entity", entityId.ToString());
        }
        
        risk.Entities.Remove(entity);

        context.SaveChanges();
    }

    public async Task<List<Risk>> GetAllAsync(string? status = null, string? notStatus = "Closed", bool includeCatalogs = true)
    {
        await using var context = dalService.GetContext();
        
        var query = context.Risks.AsQueryable().AsNoTracking();

        if (includeCatalogs)
        {
            query = query.Include(r => r.RiskCatalogs).IgnoreAutoIncludes();
        }
        
        if (status != null && notStatus != null)
        {
            query = query.Where(r => r.Status == status && r.Status != notStatus);
        }
        else if (status != null)
        {
            query = query.Where(r => r.Status == status);
        }
        else if (notStatus != null)
        {
            query = query.Where(r => r.Status != notStatus);
        }

        var risks = await query.ToListAsync();

        return risks;
    }
    
    [Obsolete("Use the GetAllAsync instead")]
    public List<Risk> GetAll(string? status = null, string? notStatus = "Closed")
    {
        List<Risk> risks;
        //new List<Risk>();

        using var context = dalService.GetContext();
        if (status != null && notStatus != null)
        {
            risks = context.Risks
                //.Include(r=>r.SourceNavigation)
                //.Include(r => r.CategoryNavigation)
                .Where(r => r.Status == status && r.Status != notStatus).ToList();
        }
        else if (status != null)
        {
            risks = context.Risks
                //.Include(r=>r.SourceNavigation)
                //.Include(r => r.CategoryNavigation)
                .Where(r => r.Status == status).ToList();
        }
        else if (notStatus != null)
        {
            risks = context.Risks
                //.Include(r=>r.SourceNavigation)
                //.Include(r => r.CategoryNavigation)
                .Where(r => r.Status != notStatus).ToList();
        }
        else
        {
            risks = context.Risks.IgnoreAutoIncludes()
                //.Include(r=>r.SourceNavigation)
                //.Include(r => r.CategoryNavigation)
                .ToList();;
        }

        return risks;
    }

    public Category GetRiskCategory(int id)
    {
        using (var contex = dalService.GetContext())
        {
            
            var cat = contex.Categories.FirstOrDefault(c => c.Value == id);

            if (cat == null)
            {
                throw new DataNotFoundException("Category", id.ToString());
            }

            return cat;
        }
    }
    
    public List<Category> GetRiskCategories()
    {
        using var context = dalService.GetContext();
        var cats = context.Categories.ToList();

        if (cats == null)
        {
            throw new DataNotFoundException("Categories", "");
        }

        return cats;
    }

    public List<Vulnerability> GetVulnerabilities(int riskId)
    {
        using var context = dalService.GetContext();
        
        var risk = context.Risks.Include(r => r.Vulnerabilities).FirstOrDefault(r=> r.Id == riskId);
        
        if (risk == null)
        {
            throw new DataNotFoundException("Risk", riskId.ToString());
        }
        return risk.Vulnerabilities.ToList();
    }

    public async Task<List<Vulnerability>> GetVulnerabilitiesAsync(int riskId, bool includeClosed = false)
    {
        await using var context = dalService.GetContext();

        var closedStatus = new List<int>
        {
            (int)IntStatus.Closed,
            (int)IntStatus.Solved,
            (int)IntStatus.Rejected,
            (int)IntStatus.Fixed,
        };
        
        Risk? risk;
        /*if(!includeClosed)
            risk = context.Risks.Include(r => r.Vulnerabilities.Where(v=> !closedStatus.Contains(v.Status) )).FirstOrDefault(r=> r.Id == riskId);
        else */
            risk = context.Risks.Include(r => r.Vulnerabilities).FirstOrDefault(r=> r.Id == riskId);
        
        
        if (risk == null)
        {
            throw new DataNotFoundException("Risk", riskId.ToString());
        }
        return risk.Vulnerabilities.Where(v=> !closedStatus.Contains(v.Status)).ToList(); 
    }

    public async Task<Tuple<int, List<Vulnerability>>> GetFilteredVulnerabilitiesAsync(int riskId, SieveModel filter)
    {
        await using var dbContext = dalService.GetContext();

        var vul = dbContext.Vulnerabilities.Include(v=> v.Risks).Where(v => v.Risks.Any(r => r.Id == riskId)).AsNoTracking();
         
        var vulnerabilities = SieveProcessor.Apply(filter, vul, applyPagination: false);
        var totalCount = vulnerabilities.Count();
        
        vulnerabilities = SieveProcessor.Apply(filter, vul); // Returns `result` after applying the sort/filter/page query in `SieveModel` to it
        return new Tuple<int, List<Vulnerability>>(totalCount, await vulnerabilities.AsParallel().ToAsyncEnumerable().ToListAsync());
    }

    public async Task<IncidentResponsePlan?> GetIncidentResponsePlanAsync(int riskId)
    {
        await using var context = dalService.GetContext();
        
        Risk? risk;
        risk = context.Risks.Include(r => r.IncidentResponsePlan).FirstOrDefault(r=> r.Id == riskId);

        if (risk == null) throw new DataNotFoundException("risk", riskId.ToString());
        
        return risk!.IncidentResponsePlan;

    }

    public async Task AssocianteRiskToIncidentResponsePlanAsync(int riskId, int incidentResponsePlanId)
    {
        await using var context = dalService.GetContext();
        
        var risk = await context.Risks.Include(r => r.IncidentResponsePlan).FirstOrDefaultAsync(r => r.Id == riskId);
        
        if(risk == null) throw new DataNotFoundException("risk", riskId.ToString());
        
        var irp = await context.IncidentResponsePlans.FirstOrDefaultAsync(irp => irp.Id == incidentResponsePlanId);
        
        if(irp == null) throw new DataNotFoundException("incident response plan", incidentResponsePlanId.ToString());
        
        risk.IncidentResponsePlan = irp;
        
        await context.SaveChangesAsync();
    }
    
    public List<CloseReason> GetRiskCloseReasons()
    {
        using var context = dalService.GetContext();
        var crs = context.CloseReasons.ToList();

        if (crs == null)
        {
            throw new DataNotFoundException("CloseReason", "");
        }

        return crs;
    }

    public Closure GetRiskClosureByRiskId(int riskId)
    {
        using var context = dalService.GetContext();
        //Let´s check if the risk exists
        var risk = context.Risks.FirstOrDefault(r => r.Id == riskId);
        if(risk == null) throw new DataNotFoundException("Risk", riskId.ToString());
        
        var closure = context.Closures.FirstOrDefault(c => c.RiskId == riskId);
        if(closure == null) throw new DataNotFoundException("Closure", riskId.ToString());
        return closure;
    }

    public bool ClosureExists(int riskId)
    {
        using var context = dalService.GetContext();
        //Let´s check if the risk exists
        var risk = context.Risks.FirstOrDefault(r => r.Id == riskId);
        if (risk == null) return false;
        
        var closure = context.Closures.FirstOrDefault(c => c.RiskId == riskId);
        return closure != null;
    }

    public Closure CreateRiskClosure(Closure closure)
    {
        using var context = dalService.GetContext();
        
        //Let´s check if the risk already has a closure
        var result = context.Closures.FirstOrDefault(c => c.RiskId == closure.RiskId);
        if(result!= null) throw new DataAlreadyExistsException("local", "Closure", closure.RiskId.ToString(),
            "Risk already has a closure");
        
        var newClosure = context.Closures.Add(closure);
        context.SaveChanges();
        return newClosure.Entity;
    }

    public void DeleteRiskClosure(int closureId)
    {
        using var context = dalService.GetContext();
        
        var result = context.Closures.FirstOrDefault(c => c.Id == closureId);
        if(result == null) throw new DataNotFoundException("Closure", closureId.ToString());
        
        context.Closures.Remove(result);
        context.SaveChanges();
    }
    
    public List<Likelihood> GetRiskProbabilities()
    {
        using (var contex = dalService.GetContext())
        {
            
            var probs = contex.Likelihoods.ToList();

            if (probs == null)
            {
                throw new DataNotFoundException("Likelihoods", "");
            }

            return probs;
        }
    }

    public List<Impact> GetRiskImpacts()
    {
        return AsyncHelper.RunSync(async () => await GetRiskImpactsAsync());
    }

    public async Task<List<Impact>> GetRiskImpactsAsync()
    {
        await using var contex = dalService.GetContext();
        var impacts = await contex.Impacts.ToListAsync();

        if (impacts == null)
        {
            throw new DataNotFoundException("Impacts", "");
        }

        return impacts;
    }

    public double GetRiskScore(int probabilityId, int impactId)
    {
        using (var contex = dalService.GetContext())
        {
            
            var score = contex.CustomRiskModelValues.Where(c => c.Likelihood == probabilityId && c.Impact == impactId).FirstOrDefault();

            if (score == null)
            {
                throw new DataNotFoundException("CustomRiskModelValues", "");
            }

            return score.Value;
        }
    }
    
    public RiskCatalog GetRiskCatalog(int id)
    {
        using (var contex = dalService.GetContext())
        {
            
            var cat = contex.RiskCatalogs.Where(c => c.Id == id).FirstOrDefault();

            if (cat == null)
            {
                throw new DataNotFoundException("Catalog", id.ToString());
            }

            return cat;
        }
    }

    public List<RiskCatalog> GetRiskCatalogs()
    {
        using (var contex = dalService.GetContext())
        {

            var cats = contex.RiskCatalogs.ToList();

            if (cats == null)
            {
                throw new DataNotFoundException("Catalog", "");
            }

            return cats;
        }
    }

    public bool SubjectExists(string subject)
    {
        using (var contex = dalService.GetContext())
        {
            var results = contex.Risks.Where(rsk => rsk.Subject == subject).Count();
            if (results > 0) return true;
            else return false;
        }
    }
    public Risk? CreateRisk(Risk risk)
    {
        using var contex = dalService.GetContext();
        risk.Id = 0;
        risk.SubmissionDate = DateTime.Now;
        risk.LastUpdate = DateTime.Now;
        risk.MitigationId = null;
        risk.Mitigation = null;
        var source = contex.Sources.Find(risk.Source);
        if (source == null) throw new DataNotFoundException("Source", "risk.Source");
        risk.SourceNavigation = source;
        var category = contex.Categories.Find(risk.Category);
        if (category == null) throw new DataNotFoundException("Category", "risk.Source");
        risk.CategoryNavigation = category;
        contex.Risks.Add(risk);
        contex.SaveChanges();
        return risk;
    }
    
    public async Task<Risk?> CreateRiskAsync(Risk risk)
    {
        await using var contex = dalService.GetContext();
        
        risk.Id = 0;
        risk.SubmissionDate = DateTime.Now;
        risk.LastUpdate = DateTime.Now;
        risk.MitigationId = null;
        risk.Mitigation = null;

        var catalogs = risk.RiskCatalogs;
        risk.RiskCatalogs = new List<RiskCatalog>();
        
        var source = await contex.Sources.FindAsync(risk.Source);
        if (source == null) throw new DataNotFoundException("Source", "risk.Source");
        risk.SourceNavigation = source;
        
        var category = await contex.Categories.FindAsync(risk.Category);
        if (category == null) throw new DataNotFoundException("Category", "risk.Source");
        risk.CategoryNavigation = category;
        
        contex.Risks.Add(risk);
        await contex.SaveChangesAsync();
        
        if (catalogs.Count > 0)
        {
            foreach (var rc in catalogs)
            {
                var catalog = await contex.RiskCatalogs.FindAsync(rc.Id);
                if (catalog == null) throw new DataNotFoundException("RiskCatalog", rc.Id.ToString());
                risk.RiskCatalogs.Add(catalog);
            }
            await contex.SaveChangesAsync();
        }
        
        return risk;
    }

    public RiskScoring? CreateRiskScoring(RiskScoring riskScoring)
    {

        
        using (var context = dalService.GetContext())
        {
            // Check if exists already
            var existing = context.RiskScorings.Where(r => r.Id == riskScoring.Id).Count();
            if(existing > 0 ) throw new DataAlreadyExistsException("main",
                "risk_scoring", riskScoring.Id.ToString(), $"Risk scoring with id:{riskScoring.Id} already exists");
            
            var scoring = context.RiskScorings.Add(riskScoring);

            var scoringHistory = new RiskScoringHistory
            {
                RiskId = riskScoring.Id,
                CalculatedRisk = riskScoring.CalculatedRisk,
                LastUpdate = DateTime.Now
            };

            context.RiskScoringHistories.Add(scoringHistory);
            
            context.SaveChanges();
            return scoring.Entity;
        }
    }

    public void SaveRiskScoring(RiskScoring riskScoring)
    {
        using (var context = dalService.GetContext())
        {
            var dbRiskScoring = context.RiskScorings.FirstOrDefault(r => r.Id == riskScoring.Id);
            if (dbRiskScoring == null) throw new Exception($"Unable to find risk scoring with id:{riskScoring.Id}");
            riskScoring.Adapt(dbRiskScoring);
            
            var scoringHistory = new RiskScoringHistory
            {
                RiskId = dbRiskScoring.Id,
                CalculatedRisk = dbRiskScoring.CalculatedRisk,
                LastUpdate = DateTime.Now
            };

            context.RiskScoringHistories.Add(scoringHistory);
            
            context.SaveChanges();
        }
    }
    
    /// <summary>
    /// Saves a existing risk to the database
    /// </summary>
    /// <param name="risk">The risk to be saved (updated)</param>
    public void SaveRisk(Risk risk)
    {
        using var context = dalService.GetContext();
        
        var dbRisk = context.Risks.Include(risk => risk.RiskCatalogs).FirstOrDefault(r => r.Id == risk.Id);
        if (dbRisk == null) throw new Exception($"Unable to find risk with id:{risk.Id}");
        
        
        dbRisk.RiskCatalogs.Clear();
        foreach (var rc in risk.RiskCatalogs)
        {
            var catalog = context.RiskCatalogs.Find(rc.Id);
            if (catalog == null) throw new DataNotFoundException("RiskCatalog", rc.Id.ToString());
            dbRisk.RiskCatalogs.Add(catalog);
        }
        //context.SaveChanges();
            
        risk.Adapt(dbRisk);
        context.SaveChanges();
    }

    public void DeleteRisk(int id)
    {
        using (var context = dalService.GetContext())
        {
            var dbRisk = context.Risks.FirstOrDefault(r => r.Id == id);
            if (dbRisk == null) throw new DataNotFoundException("simplerisk",$"Unable to find risk with id:{id}");
            context.Risks.Remove(dbRisk);
            context.SaveChanges();
        }
    }

    public void DeleteRiskScoring(int id)
    {
        using (var context = dalService.GetContext())
        {
            var dbRiskScoring = context.RiskScorings.FirstOrDefault(r => r.Id == id);
            if (dbRiskScoring == null) throw new DataNotFoundException("simplerisk",id.ToString());
            context.RiskScorings.Remove(dbRiskScoring);
            context.SaveChanges();
        }
    }
    
    public List<RiskCatalog> GetRiskCatalogs(List<int> ids)
    {
        using (var contex = dalService.GetContext())
        {

            var cats = contex.RiskCatalogs.Where(c => ids.Contains(c.Id)).ToList();

            if (cats == null)
            {
                string sids = "";
                foreach (var id in ids)
                {
                    sids += id + ",";
                }
                throw new DataNotFoundException("Catalog", sids);
            }

            return cats;
        }
    }
    
    public Source GetRiskSource(int id)
    {
        using (var contex = dalService.GetContext())
        {
            
            var src = contex.Sources.Where(c => c.Value == id).FirstOrDefault();

            if (src == null)
            {
                throw new DataNotFoundException("Source", id.ToString());
            }

            return src;
        }
    }
    
    public List<Source> GetRiskSources()
    {
        using var contex = dalService.GetContext();
        var src = contex.Sources.OrderBy(s => s.Name).ToList();

        if (src == null)
        {
            throw new DataNotFoundException("Source" , "sources is empty");
        }

        return src;
    }

    public List<Risk> GetRisksNeedingReview(string? status = null)
    {
        var risks = new List<Risk>();

        using (var contex = dalService.GetContext())
        {
            if (status != null)
            {
                risks = contex.Risks.Where(r => r.Status == status)
                    .Where(r => !contex.MgmtReviews
                        .Select(mr => mr.RiskId)
                        .Contains(r.Id)
                    ).ToList();
                
            } else risks = contex.Risks
                .Where(r => !contex.MgmtReviews
                    .Select(mr => mr.RiskId)
                    .Contains(r.Id)
                ).ToList();
            
        }
        
        return risks;
    }
    
    private bool UserHasRisksPermission(User user, string permission = "riskmanagement")
    {
        if (user.Admin) return true;

        var permissions = rolesService.GetRolePermissions(user.RoleId);
        
        var userPermissions = AsyncHelper.RunSync(async() => await usersService.GetUserPermissionsAsync(user.Value));
            //usersService.GetUserPermissions(user.Value);
        
        permissions.AddRange(userPermissions);

        if (permissions.Contains(permission)) return true;
        
        return false;
    }

    public List<RiskScoring> GetRisksScoring(List<int> ids)
    {
       return AsyncHelper.RunSync(() => GetRisksScoringAsync(ids));
    }

    public async Task<List<RiskScoring>> GetRisksScoringAsync(List<int> ids)
    {
        await using var contex = dalService.GetContext();

        var scorings =  contex.RiskScorings.ToList().Where(rs => ids.Contains(rs.Id)).ToList();
        return scorings;
    }
}