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
using Model.Governance;
using DAL.Enums;
using Sieve.Models;
using Sieve.Services;
using Tools.Helpers;

namespace ServerServices.Services;

public class RisksService(
    IDalService dalService,
    IRolesService rolesService,
    ISieveProcessor sieveProcessor,
    IUsersService usersService,
    INotificationEventPublisher notifications,
    IRiskWorkflowService workflow)
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

    public async Task<List<Risk>> GetAllAsync(string? status = null, string? notStatus = "Closed", bool includeCatalogs = true, System.Security.Claims.ClaimsPrincipal? userPrincipal = null)
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

        // Apply dynamic entity scoping filter
        query = query.ApplyEntityScope(userPrincipal);

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
        var list = await vulnerabilities.ToListAsync();
        return new Tuple<int, List<Vulnerability>>(totalCount, list);
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

    public void DeleteRiskClosure(int riskId)
    {
        using var context = dalService.GetContext();
        
        //Let´s check if the risk exists
        var risk = context.Risks.FirstOrDefault(r => r.Id == riskId);
        if (risk == null) throw new DataNotFoundException("Risk", riskId.ToString());;
        
        var result = context.Closures.FirstOrDefault(c => c.RiskId == riskId);
        if(result == null) throw new DataNotFoundException("Risk Closure", riskId.ToString());
        
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

        // Track 4.1.3 — risk.created. The score is read separately because scoring is its own row and
        // is normally written after the risk; a risk created without one notifies with no severity
        // rather than not notifying at all.
        var scoring = await contex.RiskScorings.FirstOrDefaultAsync(sc => sc.Id == risk.Id);
        await notifications.RiskCreatedAsync(risk, scoring?.CalculatedRisk);

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

    // --- Track 8 milestone 8.3.1: the state machine --------------------------------------------

    public async Task SaveRiskAsync(Risk risk)
    {
        ArgumentNullException.ThrowIfNull(risk);

        await using var context = dalService.GetContext();

        var dbRisk = await context.Risks
            .Include(r => r.RiskCatalogs)
            .FirstOrDefaultAsync(r => r.Id == risk.Id);

        if (dbRisk == null)
            throw new DataNotFoundException("local", "risks",
                new Exception($"Unable to find risk with id:{risk.Id}"));

        // The check runs before anything is mutated, against the status the row currently holds.
        // Doing it here rather than in the controller is the point: SaveRisk is the single choke
        // point every client write funnels through, and a rule enforced at one call site out of
        // several is not a rule.
        await workflow.EnsureTransitionAllowedAsync(risk.Id, dbRisk.Status, risk.Status);

        dbRisk.RiskCatalogs.Clear();
        foreach (var rc in risk.RiskCatalogs)
        {
            var catalog = await context.RiskCatalogs.FindAsync(rc.Id);
            if (catalog == null) throw new DataNotFoundException("RiskCatalog", rc.Id.ToString());
            dbRisk.RiskCatalogs.Add(catalog);
        }

        risk.Adapt(dbRisk);
        await context.SaveChangesAsync();
    }

    // --- Track 8 milestone 8.5.2: pending-risk triage ------------------------------------------

    public async Task<List<PendingRiskListing>> GetPendingRisksAsync(
        PendingRiskStatus? status = PendingRiskStatus.Pending)
    {
        await using var context = dalService.GetContext();

        var query = context.PendingRisks.AsQueryable();
        if (status != null) query = query.Where(p => p.Status == status.Value);

        var rows = await query.OrderByDescending(p => p.SubmissionDate).ThenByDescending(p => p.Id)
            .ToListAsync();

        return rows.Select(p => new PendingRiskListing
        {
            Id = p.Id,
            AssessmentId = p.AssessmentId,
            AssessmentAnswerId = p.AssessmentAnswerId,
            Subject = DecodeSubject(p.Subject),
            Score = p.Score,
            OwnerId = p.Owner,
            AffectedAssets = p.AffectedAssets,
            Comment = p.Comment,
            SubmissionDate = p.SubmissionDate,
            Status = p.Status,
            PromotedRiskId = p.PromotedRiskId,
            DismissalReason = p.DismissalReason
        }).ToList();
    }

    public async Task<Risk> PromotePendingRiskAsync(int pendingRiskId, PendingRiskPromotion edits,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(edits);

        await using var context = dalService.GetContext();

        var pending = await context.PendingRisks.FirstOrDefaultAsync(p => p.Id == pendingRiskId);
        if (pending == null)
            throw new DataNotFoundException("local", "pending_risks",
                new Exception($"Pending risk with id {pendingRiskId} not found"));

        if (pending.Status != PendingRiskStatus.Pending)
            throw new InvalidStateTransitionException(pending.Status.ToString(),
                PendingRiskStatus.Promoted.ToString(),
                "This pending risk has already been triaged. Promoting it again would create a second " +
                "risk from one assessment answer, with nothing to say they are the same finding.");

        var subject = string.IsNullOrWhiteSpace(edits.Subject) ? DecodeSubject(pending.Subject) : edits.Subject.Trim();
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidParameterException(nameof(edits.Subject),
                "The promoted risk needs a subject. The assessment answer did not carry one, so it has " +
                "to be supplied here.");

        // The category and source are required FKs on `risks`, so a promotion that does not name
        // them takes whatever the register's first rows are rather than failing on a constraint.
        var categoryId = edits.CategoryId ?? await context.Categories.Select(c => c.Value).FirstOrDefaultAsync();
        var sourceId = edits.SourceId ?? await context.Sources.Select(sr => sr.Value).FirstOrDefaultAsync();

        var risk = new Risk
        {
            Status = "New",
            StatusId = DAL.Enums.RiskStatus.New,
            Subject = subject,
            ReferenceId = $"ASMT-{pending.AssessmentId}-{pending.AssessmentAnswerId}",
            Category = categoryId,
            Source = sourceId,
            Owner = edits.OwnerId ?? pending.Owner,
            Manager = edits.ManagerId,
            SubmittedBy = actingUserId,
            EntityId = edits.EntityId,
            Assessment = pending.Comment ?? string.Empty,
            Notes = edits.Notes ?? pending.Comment ?? string.Empty,
            SubmissionDate = DateTime.UtcNow,
            LastUpdate = DateTime.UtcNow,
            RiskCatalogMapping = string.Empty,
            ThreatCatalogMapping = string.Empty
        };

        context.Risks.Add(risk);
        await context.SaveChangesAsync();

        // The scoring row is what makes the risk appear in lists, heatmaps and the review cadence.
        // A promoted risk without one is invisible, which is indistinguishable from not promoting it.
        var likelihood = edits.Likelihood ?? 2;
        var impact = edits.Impact ?? 2;

        var modelValue = await context.CustomRiskModelValues
            .FirstOrDefaultAsync(rmv => rmv.Impact == impact && rmv.Likelihood == likelihood);

        context.RiskScorings.Add(new RiskScoring
        {
            Id = risk.Id,
            ScoringMethod = 1,
            ClassicLikelihood = likelihood,
            ClassicImpact = impact,
            CalculatedRisk = modelValue != null ? Convert.ToSingle(modelValue.Value) : pending.Score
        });

        pending.Status = PendingRiskStatus.Promoted;
        pending.PromotedRiskId = risk.Id;
        pending.TriagedById = actingUserId;
        pending.TriagedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        var scoring = await context.RiskScorings.FirstOrDefaultAsync(sc => sc.Id == risk.Id);
        await notifications.RiskCreatedAsync(risk, scoring?.CalculatedRisk);

        return risk;
    }

    public async Task DismissPendingRiskAsync(int pendingRiskId, string reason, int actingUserId)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidParameterException(nameof(reason),
                "Dismissing a pending risk needs a reason. A queue drained without reasons is a queue " +
                "deleted, and the next auditor cannot tell the difference.");

        await using var context = dalService.GetContext();

        var pending = await context.PendingRisks.FirstOrDefaultAsync(p => p.Id == pendingRiskId);
        if (pending == null)
            throw new DataNotFoundException("local", "pending_risks",
                new Exception($"Pending risk with id {pendingRiskId} not found"));

        if (pending.Status != PendingRiskStatus.Pending)
            throw new InvalidStateTransitionException(pending.Status.ToString(),
                PendingRiskStatus.Dismissed.ToString(), "This pending risk has already been triaged.");

        pending.Status = PendingRiskStatus.Dismissed;
        pending.DismissalReason = reason.Trim();
        pending.TriagedById = actingUserId;
        pending.TriagedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
    }

    // --- Track 8 milestone 8.5.1: event-triggered review ---------------------------------------

    public async Task<bool> RequestReviewAsync(int riskId, string reason)
    {
        await using var context = dalService.GetContext();

        var risk = await context.Risks.FirstOrDefaultAsync(r => r.Id == riskId);
        if (risk == null)
            throw new DataNotFoundException("local", "risks",
                new Exception($"Risk with id {riskId} not found"));

        // Already flagged: keep the first reason and the first timestamp. Overwriting them would
        // make "how long has this been waiting" unanswerable, which is the number that matters.
        if (risk.ReviewRequested) return false;

        risk.ReviewRequested = true;
        risk.ReviewRequestedAt = DateTime.UtcNow;
        risk.ReviewRequestedReason = reason;
        risk.LastUpdate = DateTime.UtcNow;

        await context.SaveChangesAsync();

        return true;
    }

    public async Task<List<Risk>> GetReviewRequestedAsync()
    {
        await using var context = dalService.GetContext();

        return await context.Risks
            .Where(r => r.ReviewRequested && r.Status != "Closed")
            .Include(r => r.SourceNavigation)
            .Include(r => r.CategoryNavigation)
            .OrderBy(r => r.ReviewRequestedAt)
            .ToListAsync();
    }

    // --- Track 8 milestone 8.2.2: both scores side by side -------------------------------------

    public async Task<List<RiskScorePair>> GetScorePairsAsync(List<int>? riskIds = null)
    {
        await using var context = dalService.GetContext();

        var query = context.RiskScorings.AsQueryable();
        if (riskIds is { Count: > 0 }) query = query.Where(s => riskIds.Contains(s.Id));

        return await query
            .Select(s => new RiskScorePair
            {
                RiskId = s.Id,
                Inherent = s.CalculatedRisk,
                Residual = s.ResidualRisk,
                ContributingScore = s.ContributingScore
            })
            .ToListAsync();
    }

    /// <summary>
    /// Reads <c>pending_risks.subject</c>, which is a BLOB in the legacy schema.
    ///
    /// Track 6's convention is that text never lives in a BLOB, and this column violates it — but
    /// retyping it is a destructive migration on data this track is otherwise repairing, so the
    /// column stays and the decoding lives here. UTF-8 with a latin-1 fallback: the rows were
    /// written by the PHP-era application and are not guaranteed to be valid UTF-8.
    /// </summary>
    private static string DecodeSubject(byte[]? subject)
    {
        if (subject == null || subject.Length == 0) return string.Empty;

        try
        {
            return new System.Text.UTF8Encoding(false, true).GetString(subject);
        }
        catch (System.Text.DecoderFallbackException)
        {
            return System.Text.Encoding.Latin1.GetString(subject);
        }
    }
}
