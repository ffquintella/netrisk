using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class FixRequestsService: ServiceBase, IFixRequestsService
{
    public FixRequestsService(ILogger logger, IDalService dalService) : base(logger, dalService)
    {
    }

    /// <summary>
    /// Stores a fix request — which the website sync then pushes, with the finding's title, description, solution and
    /// host, to whoever holds its identifier (<c>SyncPushBuilder</c>, run by the job host under its unrestricted
    /// principal). So the finding must be one the caller can see: a fix request is not entity-scoped itself, and without
    /// this a scoped caller could name another entity's finding and have the job publish it (T237 review). Not found, not
    /// forbidden, so the answer does not confirm the finding exists.
    /// </summary>
    public async Task<FixRequest> CreateFixRequestAsync(FixRequest fixRequest)
    {
        await using var db = DalService.GetContext();

        if (!await db.Vulnerabilities.AnyAsync(v => v.Id == fixRequest.VulnerabilityId))
            throw new DataNotFoundException("vulnerabilities", fixRequest.VulnerabilityId.ToString(),
                new Exception($"Finding {fixRequest.VulnerabilityId} was not found."));
        
        var result =  await db.FixRequests.AddAsync(fixRequest);
        
        await db.SaveChangesAsync();
        
        return result.Entity;
    }

    public async Task<FixRequest> GetFixRequestAsync(string identifier)
    {
        await using var db = DalService.GetContext();
        
        var result = await db.FixRequests
            .Include(fr => fr.Vulnerability).ThenInclude(v => v.Host)
            .FirstOrDefaultAsync(fr => fr.Identifier == identifier);

        if (result == null)
        {
            Logger.Warning("FixRequest with identifier {identifier} not found", identifier);
            throw new DataNotFoundException("FixRequest", $"FixRequest with identifier {identifier} not found");
        }
            
        
        return result;
    }

    public async Task<FixRequest> SaveFixRequestAsync(FixRequest fixRequest)
    {
        await using var db = DalService.GetContext();
        
        var result = db.FixRequests.Update(fixRequest);
        await db.SaveChangesAsync();
        return result.Entity;
    }

    public async Task<FixRequest> GetByIdAsync(int id)
    {
        await using var db = DalService.GetContext();

        var result = await db.FixRequests
            .Include(fr => fr.Vulnerability).ThenInclude(v => v.Host)
            .FirstOrDefaultAsync(fr => fr.Id == id);

        if (result == null)
        {
            Logger.Warning("FixRequest with identifier {id} not found", id);
            throw new DataNotFoundException("FixRequest", $"FixRequest with identifier {id} not found");
        }
            
        
        return result; 
    }
    
    public async Task<List<FixRequest>> GetAllFixRequestAsync()
    {
        await using var db = DalService.GetContext();

        var result = await db.FixRequests
            .Include(fr => fr.Vulnerability).ThenInclude(v => v.Host).ToListAsync();

        if (result == null)
        {
            Logger.Warning("FixRequests not found");
            throw new DataNotFoundException("FixRequest", $"FixRequests not found");
        }
            
        
        return result; 
    }

    public async Task<List<FixRequest>> GetVulnerabilitiesFixRequestAsync(List<int> vulnerabilitiesIds)
    {
        await using var db = DalService.GetContext();
        
        var result = await db.FixRequests
            .Include(fr => fr.Vulnerability)
            .Where(fr => vulnerabilitiesIds.Contains(fr.VulnerabilityId))
            .ToListAsync();
        
        if (result == null) 
        {
            Logger.Warning("FixRequests not found");
            throw new DataNotFoundException("FixRequest", $"FixRequests not found");
        }

        return result;

    }
}